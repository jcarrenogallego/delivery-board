using System.Text.Json;
using DeliveryBoard.Mcp.Application;
using DeliveryBoard.Mcp.Tools;
using ModelContextProtocol.Protocol;

namespace DeliveryBoard.Application.UnitTests.Mcp;

public sealed class DatabaseToolsTests
{
    [Fact]
    public async Task QueryPreservesDuplicateColumnsAndStructuredValues()
    {
        using var json = JsonDocument.Parse("{\"nested\":[1,null]}");
        var expected = new QueryResult(
            [new QueryColumn("same", "integer"), new QueryColumn("same", "jsonb"), new QueryColumn("empty", "text")],
            [new object?[] { 1, json.RootElement.Clone(), null }], 1, false, null);
        var tools = new DatabaseTools(new StubDatabase(expected));

        var result = await tools.ExecuteReadQuery("SELECT 1", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal("same", root.GetProperty("columns")[0].GetProperty("name").GetString());
        Assert.Equal("same", root.GetProperty("columns")[1].GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("rows")[0][0].GetInt32());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("rows")[0][1].ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("rows")[0][2].ValueKind);
    }

    [Fact]
    public async Task DatabaseFailuresDoNotExposeInternalDetails()
    {
        var tools = new DatabaseTools(new StubDatabase(new InvalidOperationException("Password=secret; sensitive stack SQL")));

        var result = await tools.ExecuteReadQuery("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedAndCancelledQueriesHaveIdentifiableErrorCodes()
    {
        foreach (var (failure, code) in new (Exception, string)[]
        {
            (new QueryRejectedException("sensitive query"), "query_rejected"),
            (new OperationCanceledException("sensitive cancellation"), "query_cancelled")
        })
        {
            var tools = new DatabaseTools(new StubDatabase(failure));

            var result = await tools.ExecuteReadQuery("SELECT 1", CancellationToken.None);

            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            using var document = JsonDocument.Parse(text);
            Assert.Equal(code, document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain("sensitive", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void JsonSerializationIncludesTruncationAndUsesActualUtf8Bytes()
    {
        var result = new QueryResult([new QueryColumn("value", "text")], [new object?[] { "漢字🙂" }], 1, true, "response_size_limit");

        var serialized = QueryResultJson.Serialize(result);

        using var document = JsonDocument.Parse(serialized);
        Assert.Equal(1, document.RootElement.GetProperty("rowCount").GetInt32());
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("response_size_limit", document.RootElement.GetProperty("truncationReason").GetString());
        Assert.Equal("漢字🙂", document.RootElement.GetProperty("rows")[0][0].GetString());
    }

    private sealed class StubDatabase : IReadOnlyDatabase
    {
        private readonly QueryResult? result;
        private readonly Exception? failure;

        public StubDatabase(QueryResult result) => this.result = result;
        public StubDatabase(Exception failure) => this.failure = failure;

        public Task<JsonElement> GetSchemaAsync(CancellationToken cancellationToken)
            => Task.FromResult(JsonSerializer.SerializeToElement(new { schemas = Array.Empty<object>() }));

        public Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken cancellationToken)
            => failure is null ? Task.FromResult(result!) : Task.FromException<QueryResult>(failure);
    }
}
