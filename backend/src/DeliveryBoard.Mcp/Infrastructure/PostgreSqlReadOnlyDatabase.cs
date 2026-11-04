using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DeliveryBoard.Mcp.Application;
using Npgsql;

namespace DeliveryBoard.Mcp.Infrastructure;

public sealed class PostgreSqlReadOnlyDatabase(NpgsqlDataSource dataSource) : IReadOnlyDatabase
{
    public async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken cancellationToken)
    {
        ReadQueryValidator.Validate(sql);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(QueryLimits.TimeoutSeconds));
        var ct = timeout.Token;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, ct);
            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = QueryLimits.TimeoutSeconds,
                // Text format allows bounded streaming even for very large arrays and JSON values.
                AllResultTypesAreUnknown = true
            };
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new QueryColumn(reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
            var rows = new List<object?[]>();
            var result = new QueryResult(columns, rows, 0, false, null);
            var responseBytes = Encoding.UTF8.GetByteCount(QueryResultJson.Serialize(
                result with { RowCount = QueryLimits.MaxRows, Truncated = true, TruncationReason = "response_size_limit" }));
            if (responseBytes > QueryLimits.MaxResponseBytes)
                throw new QueryRejectedException("The query column metadata exceeds the response size limit.");
            while (await reader.ReadAsync(ct))
            {
                if (rows.Count == QueryLimits.MaxRows) return result with { Truncated = true, TruncationReason = "row_limit" };
                var row = new object?[reader.FieldCount];
                var remainingCharacters = QueryLimits.MaxResponseBytes;
                for (var i = 0; i < row.Length; i++)
                {
                    if (await reader.IsDBNullAsync(i, ct)) continue;
                    using var textReader = reader.GetTextReader(i);
                    var text = await ReadBoundedAsync(textReader, remainingCharacters, ct);
                    if (text is null) return result with { Truncated = true, TruncationReason = "response_size_limit" };
                    remainingCharacters -= text.Length;
                    row[i] = ConvertValue(text, columns[i].Type);
                }
                // Reserve space for the longer truncation metadata before accepting a complete row.
                var rowBytes = JsonSerializer.SerializeToUtf8Bytes(row, QueryResultJson.Options).Length + (rows.Count == 0 ? 0 : 1);
                if (responseBytes + rowBytes > QueryLimits.MaxResponseBytes)
                    return result with { Truncated = true, TruncationReason = "response_size_limit" };
                responseBytes += rowBytes;
                rows.Add(row);
                result = result with { RowCount = rows.Count };
            }
            return result;
        }
        finally
        {
            // Rollback uses its own timeout so caller cancellation cannot leave an open transaction.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(cleanup.Token);
        }
    }

    public async Task<JsonElement> GetSchemaAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(QueryLimits.TimeoutSeconds));
        await using var connection = await dataSource.OpenConnectionAsync(timeout.Token);
        await using var transaction = await connection.BeginTransactionAsync(timeout.Token);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, timeout.Token);
            await using var command = new NpgsqlCommand(SchemaSql, connection, transaction) { CommandTimeout = QueryLimits.TimeoutSeconds };
            var json = (string)(await command.ExecuteScalarAsync(timeout.Token)
                ?? throw new InvalidOperationException("Schema query returned no result."));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(cleanup.Token);
        }
    }

    private static async Task ConfigureTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '30s'; SET LOCAL lock_timeout = '5s'; SET LOCAL search_path = public, pg_catalog; SET LOCAL DateStyle = 'ISO, YMD'; SET LOCAL TimeZone = 'UTC'; SET LOCAL standard_conforming_strings = on", connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> ReadBoundedAsync(TextReader reader, int maxCharacters, CancellationToken ct)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxCharacters - builder.Length + 1)), ct);
            if (count == 0) return builder.ToString();
            if (builder.Length + count > maxCharacters) return null;
            builder.Append(buffer, 0, count);
        }
    }

    private static object ConvertValue(string text, string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal)) return ParseArray(text, type[..^2]);
        if (type is "json" or "jsonb")
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        if (type is "boolean" or "bool") return text == "t";
        if (type is "smallint" or "integer" or "bigint" or "int2" or "int4" or "int8")
            return long.Parse(text, CultureInfo.InvariantCulture);
        if (type is "numeric" or "decimal" && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric)) return numeric;
        if (type is "real" or "double precision" or "float4" or "float8"
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) && double.IsFinite(floating)) return floating;
        return text;
    }

    private static object?[] ParseArray(string text, string elementType)
    {
        var i = text.IndexOf('{');
        if (i < 0) return [text];
        return ReadArray();

        object?[] ReadArray()
        {
            var values = new List<object?>();
            i++;
            while (i < text.Length && text[i] != '}')
            {
                if (text[i] == '{') values.Add(ReadArray());
                else
                {
                    var quoted = text[i] == '"';
                    if (quoted) i++;
                    var value = new StringBuilder();
                    while (i < text.Length)
                    {
                        var c = text[i++];
                        if (c == '\\' && i < text.Length) { value.Append(text[i++]); continue; }
                        if (quoted && c == '"') break;
                        if (!quoted && c is ',' or '}') { i--; break; }
                        value.Append(c);
                    }
                    var item = value.ToString();
                    values.Add(!quoted && item == "NULL" ? null : ConvertValue(item, elementType));
                }
                if (i < text.Length && text[i] == ',') i++;
            }
            i++;
            return values.ToArray();
        }
    }

    private const string SchemaSql = """
        SELECT jsonb_build_object('schemas', COALESCE(jsonb_agg(schema_data ORDER BY schema_data->>'name'), '[]'::jsonb))::text
        FROM (
          SELECT jsonb_build_object(
            'name', n.nspname,
            'tables', COALESCE((SELECT jsonb_agg(jsonb_build_object(
              'name', c.relname, 'kind', c.relkind,
              'comment', obj_description(c.oid, 'pg_class'),
              'definition', CASE WHEN c.relkind IN ('v', 'm') THEN pg_get_viewdef(c.oid, true) ELSE NULL END,
              'columns', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'name', a.attname, 'ordinal', a.attnum, 'type', format_type(a.atttypid, a.atttypmod),
                'nullable', NOT a.attnotnull, 'default', pg_get_expr(d.adbin, d.adrelid),
                'identity', a.attidentity, 'generated', a.attgenerated,
                'comment', col_description(c.oid, a.attnum)) ORDER BY a.attnum)
                FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped), '[]'::jsonb),
              'constraints', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'name', con.conname, 'type', con.contype, 'definition', pg_get_constraintdef(con.oid, true),
                'referencedSchema', rn.nspname, 'referencedTable', rc.relname,
                'columns', (SELECT jsonb_agg(a.attname ORDER BY k.ord) FROM unnest(con.conkey) WITH ORDINALITY k(num, ord) JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = k.num),
                'referencedColumns', (SELECT jsonb_agg(a.attname ORDER BY k.ord) FROM unnest(con.confkey) WITH ORDINALITY k(num, ord) JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.num)) ORDER BY con.conname)
                FROM pg_constraint con LEFT JOIN pg_class rc ON rc.oid = con.confrelid LEFT JOIN pg_namespace rn ON rn.oid = rc.relnamespace
                WHERE con.conrelid = c.oid), '[]'::jsonb),
              'indexes', COALESCE((SELECT jsonb_agg(jsonb_build_object('name', ic.relname, 'definition', pg_get_indexdef(ix.indexrelid), 'unique', ix.indisunique, 'primary', ix.indisprimary) ORDER BY ic.relname)
                FROM pg_index ix JOIN pg_class ic ON ic.oid = ix.indexrelid WHERE ix.indrelid = c.oid), '[]'::jsonb)
              ) ORDER BY c.relname) FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind IN ('r', 'p', 'v', 'm', 'f')), '[]'::jsonb),
            'sequences', COALESCE((SELECT jsonb_agg(jsonb_build_object('name', c.relname, 'type', format_type(s.seqtypid, NULL), 'start', s.seqstart, 'increment', s.seqincrement, 'min', s.seqmin, 'max', s.seqmax, 'cache', s.seqcache, 'cycle', s.seqcycle) ORDER BY c.relname)
              FROM pg_class c JOIN pg_sequence s ON s.seqrelid = c.oid WHERE c.relnamespace = n.oid), '[]'::jsonb),
            'types', COALESCE((SELECT jsonb_agg(jsonb_build_object('name', t.typname, 'kind', t.typtype,
              'baseType', CASE WHEN t.typbasetype <> 0 THEN format_type(t.typbasetype, t.typtypmod) ELSE NULL END,
              'nullable', NOT t.typnotnull, 'default', t.typdefault,
              'enumValues', (SELECT jsonb_agg(e.enumlabel ORDER BY e.enumsortorder) FROM pg_enum e WHERE e.enumtypid = t.oid),
              'constraints', (SELECT jsonb_agg(pg_get_constraintdef(co.oid, true) ORDER BY co.conname) FROM pg_constraint co WHERE co.contypid = t.oid)) ORDER BY t.typname)
              FROM pg_type t WHERE t.typnamespace = n.oid AND t.typtype IN ('e', 'd', 'c', 'r', 'm') AND t.typelem = 0), '[]'::jsonb)
          ) AS schema_data
          FROM pg_namespace n WHERE n.nspname <> 'information_schema' AND n.nspname !~ '^pg_'
        ) schemas
        """;
}
