using System.ComponentModel;
using System.Text.Json;
using DeliveryBoard.Mcp.Application;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Npgsql;

namespace DeliveryBoard.Mcp.Tools;

[McpServerToolType]
public sealed class DatabaseTools(IReadOnlyDatabase database)
{
    [McpServerTool(Name = "get_database_schema", ReadOnly = true), Description("Returns the complete PostgreSQL user schema, including tables, views, columns, constraints, indexes, sequences and types.")]
    public Task<CallToolResult> GetDatabaseSchema(CancellationToken cancellationToken) =>
        RunAsync(async () => (await database.GetSchemaAsync(cancellationToken)).GetRawText());

    [McpServerTool(Name = "execute_read_query", ReadOnly = true), Description("Executes a single read-only PostgreSQL query. Returns at most 1000 complete rows and 1 MiB of JSON within 30 seconds; no pagination.")]
    public Task<CallToolResult> ExecuteReadQuery([Description("One SELECT, WITH, VALUES or TABLE statement.")] string sql, CancellationToken cancellationToken) =>
        RunAsync(async () => QueryResultJson.Serialize(await database.ExecuteQueryAsync(sql, cancellationToken)));

    private static async Task<CallToolResult> RunAsync(Func<Task<string>> action)
    {
        try { return new() { Content = [new TextContentBlock { Text = await action() }] }; }
        catch (QueryRejectedException) { return Error("query_rejected", "Only one read-only SQL statement is allowed."); }
        catch (OperationCanceledException) { return Error("query_cancelled", "The operation was cancelled or exceeded its time limit."); }
        catch (PostgresException e) when (e.SqlState == "57014") { return Error("query_timeout", "The query exceeded its time limit."); }
        catch (PostgresException e) when (e.SqlState is "25006" or "42501") { return Error("read_only_violation", "The database rejected an operation outside read-only permissions."); }
        catch (NpgsqlException) { return Error("database_error", "The database could not complete the read operation."); }
        catch (Exception) { return Error("internal_error", "The read operation could not be completed."); }
    }

    private static CallToolResult Error(string code, string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = new { code, message } }) }]
    };
}
