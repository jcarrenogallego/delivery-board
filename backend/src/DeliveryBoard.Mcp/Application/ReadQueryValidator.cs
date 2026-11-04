namespace DeliveryBoard.Mcp.Application;

/// <summary>Conservative lexical gate; PostgreSQL privileges and READ ONLY enforce the boundary.</summary>
public static class ReadQueryValidator
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "COPY", "CREATE", "ALTER", "DROP", "TRUNCATE",
        "GRANT", "REVOKE", "COMMENT", "VACUUM", "ANALYZE", "REINDEX", "CLUSTER", "REFRESH",
        "CALL", "DO", "SET", "RESET", "DISCARD", "BEGIN", "START", "COMMIT", "END", "ROLLBACK",
        "SAVEPOINT", "RELEASE", "PREPARE", "EXECUTE", "DEALLOCATE", "LOCK", "LISTEN", "UNLISTEN",
        "NOTIFY", "INTO", "SECURITY", "PG_NOTIFY", "SET_CONFIG", "LO_IMPORT", "LO_EXPORT",
        "PG_TERMINATE_BACKEND", "PG_CANCEL_BACKEND", "DBLINK", "NEXTVAL", "SETVAL"
    };

    public static void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > QueryLimits.MaxResponseBytes)
            Reject();
        var tokens = new List<string>();
        var ended = false;
        string? identifier = null;
        for (var i = 0; i < sql.Length;)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] is not '\n' and not '\r') i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var depth = 1;
                i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; }
                    else if (i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }
                if (depth != 0) Reject();
                continue;
            }
            if (ended) Reject();
            if (c == ';') { ended = true; i++; continue; }
            if (c is '\'' or '"')
            {
                var quote = c;
                var escape = quote == '\'' && i > 0 && (sql[i - 1] is 'E' or 'e');
                var start = ++i;
                var closed = false;
                while (i < sql.Length)
                {
                    if (escape && sql[i] == '\\') { i += 2; continue; }
                    if (sql[i] == quote)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == quote) { i += 2; continue; }
                        if (quote == '"')
                        {
                            identifier = sql[start..i].Replace("\"\"", "\"", StringComparison.Ordinal);
                        }
                        i++; closed = true; break;
                    }
                    i++;
                }
                if (!closed) Reject();
                continue;
            }
            if (c == '$')
            {
                var end = i + 1;
                while (end < sql.Length && (char.IsAsciiLetterOrDigit(sql[end]) || sql[end] == '_')) end++;
                if (end < sql.Length && sql[end] == '$')
                {
                    var delimiter = sql[i..(end + 1)];
                    var close = sql.IndexOf(delimiter, end + 1, StringComparison.Ordinal);
                    if (close < 0) Reject();
                    i = close + delimiter.Length; continue;
                }
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = i++;
                while (i < sql.Length && (char.IsAsciiLetterOrDigit(sql[i]) || sql[i] is '_' or '$')) i++;
                var token = sql[start..i];
                Check(token);
                identifier = token;
                tokens.Add(token);
                continue;
            }
            if (c == '(' && identifier is not null) Check(identifier);
            // PostgreSQL U& quoted identifiers can disguise function names with Unicode escapes.
            // Reject this optional spelling rather than evaluating escaped executable names.
            if (c == '&' && identifier?.Equals("U", StringComparison.OrdinalIgnoreCase) == true) Reject();
            if (c != '.') identifier = null;
            i++;
        }
        if (tokens.Count == 0 || !(tokens[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || tokens[0].Equals("WITH", StringComparison.OrdinalIgnoreCase)
            || tokens[0].Equals("VALUES", StringComparison.OrdinalIgnoreCase)
            || tokens[0].Equals("TABLE", StringComparison.OrdinalIgnoreCase))) Reject();
    }

    private static void Check(string token)
    {
        if (Forbidden.Contains(token) || token.StartsWith("pg_advisory_", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("pg_try_advisory_", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("dblink_", StringComparison.OrdinalIgnoreCase)
            || token.Equals("pg_logical_emit_message", StringComparison.OrdinalIgnoreCase)) Reject();
    }

    private static void Reject() => throw new QueryRejectedException("Only one read-only SQL statement is allowed.");
}
