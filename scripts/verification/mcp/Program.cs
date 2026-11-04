using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DeliveryBoard.Infrastructure.Persistence;
using DeliveryBoard.Mcp.Application;
using DeliveryBoard.Mcp.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

// Both connections must explicitly target a disposable, isolated verification database.
var adminConnection = Required("MCP_VERIFICATION_ADMIN_CONNECTION");
var readerConnection = Required("MCP_VERIFICATION_READER_CONNECTION");
var endpoint = new Uri(Required("MCP_VERIFICATION_URL"));
var adminSettings = new NpgsqlConnectionStringBuilder(adminConnection);
var readerSettings = new NpgsqlConnectionStringBuilder(readerConnection);
Check(adminSettings.Database == readerSettings.Database && adminSettings.Host == readerSettings.Host && adminSettings.Port == readerSettings.Port,
    "Administrative and reader connections must target the same isolated database.");
Check(adminSettings.Database?.Contains("mcp_verify", StringComparison.Ordinal) == true,
    "Verification database name must contain mcp_verify; no ambient application database is accepted.");
Check(readerSettings.Username == "mcp_reader", "Reader connection must use mcp_reader.");
Check(endpoint.IsLoopback, "Verification MCP endpoint must be loopback.");

await using var admin = NpgsqlDataSource.Create(adminConnection);
var schema = "mcp_verify_" + Guid.NewGuid().ToString("N");
var qualified = $"\"{schema}\"";
var passed = 0;
try
{
    await Execute($"""
        CREATE SCHEMA {qualified};
        CREATE TABLE {qualified}."Parent" ("Id" integer PRIMARY KEY, "Label" text NOT NULL DEFAULT 'seed');
        CREATE TABLE {qualified}."Child" ("Id" integer PRIMARY KEY, "ParentId" integer REFERENCES {qualified}."Parent"("Id"));
        CREATE INDEX "ChildParentIndex" ON {qualified}."Child"("ParentId");
        CREATE VIEW {qualified}."ParentView" AS SELECT * FROM {qualified}."Parent";
        CREATE SEQUENCE {qualified}."Counter";
        INSERT INTO {qualified}."Parent" VALUES (1, 'initial');
        INSERT INTO {qualified}."Child" VALUES (2, 1);
        CREATE FUNCTION {qualified}."attempt_write"() RETURNS integer LANGUAGE plpgsql AS $$
        BEGIN INSERT INTO {qualified}."Parent" VALUES (99, 'forbidden'); RETURN 99; END $$;
        """);
    await McpReaderProvisioner.ProvisionAsync(adminConnection, readerSettings.Password!);
    await McpReaderProvisioner.ProvisionAsync(adminConnection, readerSettings.Password!);
    await Execute("COMMENT ON ROLE mcp_reader IS 'unmanaged verification role'");
    try
    {
        await ProvisioningRejects();
        await using var marker = admin.CreateCommand("SELECT shobj_description(oid, 'pg_authid') FROM pg_roles WHERE rolname='mcp_reader'");
        Check(Equals(await marker.ExecuteScalarAsync(), "unmanaged verification role"), "Unmanaged role is not changed by provisioning.");
    }
    finally { await Execute("COMMENT ON ROLE mcp_reader IS 'DeliveryBoard MCP reader v1'"); }
    await Execute($"GRANT INSERT ON {qualified}.\"Parent\" TO mcp_reader");
    try
    {
        await ProvisioningRejects();
        await using var grant = admin.CreateCommand($"SELECT has_table_privilege('mcp_reader', '{qualified}.\"Parent\"', 'INSERT')");
        Check(await grant.ExecuteScalarAsync() is true, "Unsafe role grants are preserved when provisioning refuses takeover.");
    }
    finally { await Execute($"REVOKE INSERT ON {qualified}.\"Parent\" FROM mcp_reader"); }
    await Execute($"CREATE TABLE {qualified}.\"FutureTable\" (\"Id\" integer); INSERT INTO {qualified}.\"FutureTable\" VALUES (7)");
    await using var reader = NpgsqlDataSource.Create(readerConnection);
    var database = new PostgreSqlReadOnlyDatabase(reader);

    var permissions = await database.ExecuteQueryAsync("SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls FROM pg_roles WHERE rolname = current_user", default);
    Check(permissions.Rows.Count == 1 && permissions.Rows[0].All(value => value is false), "Reader has no administrative attributes.");
    Pass("idempotent provisioning, unmanaged/unsafe role rejection and role attributes");

    var metadata = (await database.GetSchemaAsync(default)).GetRawText();
    foreach (var expected in new[] { schema, "Parent", "ParentId", "ChildParentIndex", "ParentView", "Counter", "FutureTable" })
        Check(metadata.Contains(expected, StringComparison.Ordinal), $"Schema includes {expected}.");
    Pass("real schema includes quoted tables, columns, keys, relations, indexes, view and sequence");

    var joined = await database.ExecuteQueryAsync($"WITH p AS (SELECT * FROM {qualified}.\"Parent\") SELECT p.\"Id\", c.\"Id\" FROM p JOIN {qualified}.\"Child\" c ON c.\"ParentId\" = p.\"Id\"", default);
    Check(joined.RowCount == 1 && joined.Columns[0].Name == joined.Columns[1].Name, "CTE join preserves duplicate columns.");
    var future = await database.ExecuteQueryAsync($"SELECT * FROM {qualified}.\"FutureTable\"", default);
    Check(future.RowCount == 1, "Future tables receive default SELECT privileges.");
    var empty = await database.ExecuteQueryAsync("SELECT 1 WHERE false", default);
    Check(empty.RowCount == 0 && !empty.Truncated && empty.Columns.Count == 1, "Empty queries retain columns.");
    var types = await database.ExecuteQueryAsync("SELECT NULL::text, 'c71ae5cb-1f8e-4e14-af6a-64d46cc66769'::uuid, ARRAY[1,2,NULL], '{\"value\":[1,null]}'::jsonb, DATE '2026-10-20', true, 12.5::numeric", default);
    using (var typedJson = JsonDocument.Parse(QueryResultJson.Serialize(types)))
    {
        var row = typedJson.RootElement.GetProperty("rows")[0];
        Check(row[0].ValueKind == JsonValueKind.Null && row[1].GetString() == "c71ae5cb-1f8e-4e14-af6a-64d46cc66769", "Null and UUID serialization.");
        Check(row[2].ValueKind == JsonValueKind.Array && row[2][2].ValueKind == JsonValueKind.Null, "Array serialization.");
        Check(row[3].ValueKind == JsonValueKind.Object && row[4].GetString()!.StartsWith("2026-10-20", StringComparison.Ordinal), "JSON and date serialization.");
        Check(row[5].GetBoolean() && row[6].GetDecimal() == 12.5m, "Boolean and numeric serialization.");
    }
    Pass("CTE, joins, duplicate names, empty results and PostgreSQL types");

    foreach (var sql in new[]
    {
        $"INSERT INTO {qualified}.\"Parent\" VALUES (3,'blocked')",
        $"UPDATE {qualified}.\"Parent\" SET \"Label\"='blocked'",
        $"DELETE FROM {qualified}.\"Parent\"",
        "SELECT 1; SELECT 2",
        $"WITH x AS (DELETE FROM {qualified}.\"Parent\" RETURNING *) SELECT * FROM x",
        $"SELECT {qualified}.\"attempt_write\"()",
        "COMMIT; SET default_transaction_read_only = off",
        $"SELECT * INTO {qualified}.\"Copy\" FROM {qualified}.\"Parent\"",
        $"SELECT nextval('{qualified}.\"Counter\"')"
    })
        await Rejects(() => database.ExecuteQueryAsync(sql, default));
    var unchanged = await database.ExecuteQueryAsync($"SELECT \"Label\" FROM {qualified}.\"Parent\"", default);
    Check(unchanged.RowCount == 1 && Equals(unchanged.Rows[0][0], "initial"), "Write attempts did not alter fixtures.");
    await using (var connection = await reader.OpenConnectionAsync())
    await using (var command = new NpgsqlCommand($"INSERT INTO {qualified}.\"Parent\" VALUES (4,'blocked')", connection))
        await Rejects(async () => { await command.ExecuteNonQueryAsync(); });
    Pass("DML, chained statements, writable CTE, function writes, transaction escapes, SELECT INTO and sequences rejected");

    var limit = await database.ExecuteQueryAsync("SELECT generate_series(1,1001)", default);
    Check(limit.RowCount == 1000 && limit.Truncated, "Row limit includes exactly 1000 complete rows.");
    var exactly = await database.ExecuteQueryAsync("SELECT generate_series(1,1000)", default);
    Check(exactly.RowCount == 1000 && !exactly.Truncated, "Exactly 1000 rows is not truncated.");
    var bytes = await database.ExecuteQueryAsync("SELECT repeat('漢字🙂',10000) FROM generate_series(1,100)", default);
    Check(bytes.Truncated && bytes.RowCount < 100, "UTF8 byte limit truncates complete rows.");
    Check(Encoding.UTF8.GetByteCount(QueryResultJson.Serialize(bytes)) <= 1_048_576, "Serialized result stays within 1 MiB.");
    var oversized = await database.ExecuteQueryAsync("SELECT repeat('x',1100000)", default);
    Check(oversized.Truncated && oversized.RowCount == 0, "Oversized first row is omitted entirely.");
    Pass("1000-row and 1-MiB boundaries");

    using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        await Rejects(() => database.ExecuteQueryAsync("SELECT pg_sleep(10)", cancellation.Token));
    var elapsed = Stopwatch.StartNew();
    await Rejects(() => database.ExecuteQueryAsync("SELECT pg_sleep(40)", default));
    Check(elapsed.Elapsed >= TimeSpan.FromSeconds(25) && elapsed.Elapsed < TimeSpan.FromSeconds(37), "Server query timeout is 30 seconds.");
    Check((await database.ExecuteQueryAsync("SELECT 1", default)).RowCount == 1, "Connections recover after timeout and cancellation.");
    Pass("cancellation, 30-second timeout and pool recovery");

    await using var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint });
    await using var client = await McpClient.CreateAsync(transport);
    var tools = await client.ListToolsAsync();
    Check(tools.Count == 2 && tools.Select(tool => tool.Name).Order().SequenceEqual(new[] { "execute_read_query", "get_database_schema" }), "Exactly the two intended tools are discoverable.");
    var schemaResult = await client.CallToolAsync("get_database_schema");
    Check(schemaResult.IsError != true && Text(schemaResult).Contains(schema, StringComparison.Ordinal), "Schema tool reads real fixture metadata over HTTP.");
    var readResult = await client.CallToolAsync("execute_read_query", new Dictionary<string, object?> { ["sql"] = "SELECT 42 AS answer" });
    Check(readResult.IsError != true && Text(readResult).Contains("42", StringComparison.Ordinal), "Query tool returns data over HTTP.");
    var rejected = await client.CallToolAsync("execute_read_query", new Dictionary<string, object?> { ["sql"] = "DELETE FROM missing" });
    Check(rejected.IsError == true && !Text(rejected).Contains(readerSettings.Password!, StringComparison.Ordinal), "Tool errors are identified and credentials stay private.");
    var timeout = await client.CallToolAsync("execute_read_query", new Dictionary<string, object?> { ["sql"] = "SELECT pg_sleep(40)" });
    Check(timeout.IsError == true, "Timeout is an identifiable MCP error result.");
    var recovered = await client.CallToolAsync("execute_read_query", new Dictionary<string, object?> { ["sql"] = "SELECT 1" });
    Check(recovered.IsError != true, "MCP recovers after timeout.");
    using (var http = new HttpClient())
    {
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "/health"));
        crossOrigin.Headers.Add("Origin", "https://untrusted.example");
        using var originResponse = await http.SendAsync(crossOrigin);
        Check(originResponse.StatusCode == System.Net.HttpStatusCode.Forbidden, "Cross-origin HTTP access is rejected.");
        using var foreignHost = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "/health"));
        foreignHost.Headers.Host = "untrusted.example";
        using var hostResponse = await http.SendAsync(foreignHost);
        Check(hostResponse.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest, "Foreign Host header is rejected.");
    }
    Pass("real MCP SDK HTTP discovery, schema, read query and error result");
    Console.WriteLine($"Passed: {passed}, Failed: 0, Skipped: 0");
}
finally
{
    await Execute($"DROP SCHEMA IF EXISTS {qualified} CASCADE");
}

async Task Execute(string sql)
{
    await using var command = admin.CreateCommand(sql);
    await command.ExecuteNonQueryAsync();
}
async Task ProvisioningRejects()
{
    try { await McpReaderProvisioner.ProvisionAsync(adminConnection, readerSettings.Password!); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Provisioning unexpectedly accepted an unmanaged or unsafe role.");
}
void Pass(string scenario) { passed++; Console.WriteLine($"PASS {scenario}"); }
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Set {name} explicitly; verification never skips missing prerequisites.");
static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
static async Task Rejects(Func<Task> action)
{
    try { await action(); }
    catch (Exception exception) when (exception is not InvalidOperationException) { return; }
    throw new InvalidOperationException("Unsafe or cancelled query unexpectedly succeeded.");
}
