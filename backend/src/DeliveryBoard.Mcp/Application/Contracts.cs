using System.Text.Json;

namespace DeliveryBoard.Mcp.Application;

public interface IReadOnlyDatabase
{
    Task<JsonElement> GetSchemaAsync(CancellationToken cancellationToken);
    Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken cancellationToken);
}

public sealed record QueryColumn(string Name, string Type);
public sealed record QueryResult(IReadOnlyList<QueryColumn> Columns, IReadOnlyList<object?[]> Rows,
    int RowCount, bool Truncated, string? TruncationReason);

public static class QueryLimits
{
    public const int MaxRows = 1_000;
    public const int MaxResponseBytes = 1_048_576;
    public const int TimeoutSeconds = 30;
}

public static class QueryResultJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(QueryResult result) => JsonSerializer.Serialize(result, Options);
}

public sealed class QueryRejectedException(string message) : Exception(message);
