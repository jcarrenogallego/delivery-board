using System.Runtime.CompilerServices;
using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Xunit;

namespace DeliveryBoard.Assistant.Tests;

public sealed class ChatServiceTests
{
    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    public async Task ToolRoundTripsKeepInternalResultsOutOfPublicText(string provider)
    {
        var client = new FakeClient((round, _) => round == 1 ? [ToolCall()] :
            [new(ChatRole.Assistant, "There are "), new(ChatRole.Assistant, "three boards.")]);
        var factory = new FakeClientFactory(client);
        var sessions = new FakeSessionFactory();
        var service = Create(factory, sessions);

        var text = await Collect(service.StreamAsync(provider, [new("user", "How many boards?")]));

        Assert.Equal("There are three boards.", string.Concat(text));
        Assert.Equal(provider, factory.Provider);
        Assert.Equal(2, client.History.Count);
        Assert.Equal(ChatRole.System, client.History[0][0].Role);
        Assert.Contains(client.History[1], message => message.Role == ChatRole.Tool);
        Assert.Equal("get_database_schema", Assert.Single(sessions.Sessions).InvokedName);
        Assert.True(sessions.Sessions[0].Disposed);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task ToolLimitFailsInsteadOfReturningSuccessForToolOnlyResponse()
    {
        var client = new FakeClient((_, _) => [ToolCall()]);
        var sessions = new FakeSessionFactory();
        var service = Create(new(client), sessions, maxRounds: 2);

        var error = await Assert.ThrowsAsync<AssistantException>(() =>
            Collect(service.StreamAsync("ollama", [new("user", "Question")])));

        Assert.Equal("tool_round_limit", error.Code);
        Assert.Equal(3, sessions.Sessions[0].Calls);
        Assert.True(sessions.Sessions[0].Disposed);
    }

    [Fact]
    public async Task McpFailureStopsBeforeTheModelCanInventAnAnswer()
    {
        var client = new FakeClient((_, _) => [ToolCall()]);
        var sessions = new FakeSessionFactory { FailInvocation = true };
        var service = Create(new(client), sessions);

        var error = await Assert.ThrowsAsync<AssistantException>(() =>
            Collect(service.StreamAsync("openai", [new("user", "Question")])));

        Assert.Equal("mcp_tool_failure", error.Code);
        Assert.Empty(client.History);
    }

    [Fact]
    public async Task CancellationReachesTheProviderAndDisposesRequestResources()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeClient((_, _) => [new(ChatRole.Assistant, "first"), new(ChatRole.Assistant, "second")]);
        var sessions = new FakeSessionFactory();
        var service = Create(new(client), sessions);
        await using var stream = service.StreamAsync("ollama", [new("user", "Question")], cancellation.Token)
            .GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.MoveNextAsync());
        Assert.Equal(cancellation.Token, client.Token);
        Assert.True(sessions.Sessions[0].Disposed);
    }

    [Fact]
    public async Task ConcurrentRequestsHaveIndependentHistoriesAndMcpSessions()
    {
        var sessions = new FakeSessionFactory();
        var factory = new IndependentClientFactory();
        var service = new ChatService(factory, sessions, Options.Create(new AssistantOptions()));

        var answers = await Task.WhenAll(
            Collect(service.StreamAsync("ollama", [new("user", "one")])),
            Collect(service.StreamAsync("openai", [new("user", "two")])));

        Assert.Equal("one", string.Concat(answers[0]));
        Assert.Equal("two", string.Concat(answers[1]));
        Assert.Equal(2, sessions.Sessions.Count);
        Assert.All(sessions.Sessions, session => Assert.True(session.Disposed));
        Assert.All(sessions.Sessions, session => Assert.Equal(1, session.Calls));
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    public async Task AnaQuestionUsesSchemaBeforeGeneratingSqlAndReturnsOnlyNaturalLanguage(string provider)
    {
        const string sql = "SELECT COUNT(*) AS \"total\" FROM \"public\".\"work_items\" WHERE \"Owner\" ILIKE 'Ana'";
        List<string> order = [];
        var sessions = new FakeSessionFactory { OnInvoke = (name, arguments) =>
        {
            order.Add(name);
            if (name == "get_database_schema") return McpJson("{\"schemas\":[{\"name\":\"public\",\"tables\":[{\"name\":\"work_items\",\"columns\":[{\"name\":\"Owner\"}]}]}]}");
            Assert.Equal(sql, arguments!["sql"]);
            return McpJson("{\"rows\":[{\"total\":3}]}");
        }};
        var client = new FakeClient((round, history) =>
        {
            order.Add("model");
            var schemaCall = history.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
                .First(call => call.Name == "get_database_schema");
            Assert.Contains(history, message => message.Role == ChatRole.Tool && message.Contents
                .OfType<FunctionResultContent>().Any(result => result.CallId == schemaCall.CallId
                    && result.Result is JsonElement json
                    && json.GetProperty("schemas")[0].GetProperty("tables")[0].GetProperty("columns")[0]
                        .GetProperty("name").GetString() == "Owner"));
            Assert.DoesNotContain("public.work_items", history[0].Text);
            Assert.Contains("ALWAYS double-quote EVERY schema, table, and column identifier", history[0].Text);
            Assert.Contains(history, message => message.Role == ChatRole.User && message.Text == "cuántas tareas tiene Ana");
            if (round == 2)
                Assert.Contains(history, message => message.Role == ChatRole.Tool && message.Contents
                    .OfType<FunctionResultContent>().Any(result => result.CallId == "query" && result.Result is JsonElement json
                        && json.GetProperty("rows")[0].GetProperty("total").GetInt32() == 3));
            return round == 1
                ? [new(ChatRole.Assistant, [new FunctionCallContent("query", "execute_read_query",
                    new Dictionary<string, object?> { ["sql"] = sql })])]
                : [new(ChatRole.Assistant, "Ana tiene 3 tareas.")];
        });

        var answer = await Collect(Create(new(client), sessions).StreamAsync(provider,
            [new("user", "cuántas tareas tiene Ana")]));

        Assert.Equal(new[] { "get_database_schema", "model", "execute_read_query", "model" }, order);
        Assert.Equal("Ana tiene 3 tareas.", string.Concat(answer));
    }

    private static CallToolResult McpJson(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    [Fact]
    public async Task MissingSchemaFailsBeforeFirstModelCall()
    {
        var client = new FakeClient((_, _) => [new(ChatRole.Assistant, "Must not run")]);
        var sessions = new FakeSessionFactory { HasSchema = false };
        var error = await Assert.ThrowsAsync<AssistantException>(() => Collect(Create(new(client), sessions)
            .StreamAsync("ollama", [new("user", "cuántas tareas tiene Ana")])));
        Assert.Empty(client.History);
        Assert.Equal(new("get_database_schema", "tool_not_available", "tool"), error.McpDiagnostic);
        Assert.True(sessions.Sessions[0].Disposed);
    }

    [Fact]
    public async Task SchemaCancellationStopsBeforeModelAndDisposesResources()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeClient((_, _) => [new(ChatRole.Assistant, "Must not run")]);
        var sessions = new FakeSessionFactory { OnInvoke = (_, _) =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return null;
        }};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect(Create(new(client), sessions)
            .StreamAsync("ollama", [new("user", "Question")], cancellation.Token)));
        Assert.Empty(client.History);
        Assert.True(sessions.Sessions[0].Disposed);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task ProviderConnectionFailuresHaveSafeUnavailableError()
    {
        var client = new FakeClient((_, _) => throw new HttpRequestException("sensitive internal hostname"));
        var service = Create(new(client), new());
        var error = await Assert.ThrowsAsync<AssistantException>(() =>
            Collect(service.StreamAsync("ollama", [new("user", "Question")])));
        Assert.Equal(503, error.StatusCode);
        Assert.DoesNotContain("sensitive", error.SafeMessage);
    }

    private static ChatService Create(FakeClientFactory factory, FakeSessionFactory sessions, int maxRounds = 8)
        => new(factory, sessions, Options.Create(new AssistantOptions { MaxToolRounds = maxRounds }));

    private static ChatResponseUpdate ToolCall() => new(ChatRole.Assistant,
        [new FunctionCallContent("call1", "get_database_schema", new Dictionary<string, object?>())]);

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> stream)
    {
        List<string> items = [];
        await foreach (var item in stream) items.Add(item);
        return items;
    }

    private sealed class FakeClientFactory(FakeClient client) : IAssistantChatClientFactory
    {
        public string? Provider { get; private set; }
        public ChatProviderMetadata GetProvider(string provider) => new(provider, "test-model");
        public IChatClient Create(string provider) { Provider = provider; return client; }
    }

    private sealed class IndependentClientFactory : IAssistantChatClientFactory
    {
        public ChatProviderMetadata GetProvider(string provider) => new(provider, "test-model");
        public IChatClient Create(string provider) => new FakeClient((_, history) =>
            [new(ChatRole.Assistant, history.Last(message => message.Role == ChatRole.User).Text)]);
    }

    private sealed class FakeClient(Func<int, ChatMessage[], ChatResponseUpdate[]> response) : IChatClient
    {
        public List<ChatMessage[]> History { get; } = [];
        public bool Disposed { get; private set; }
        public CancellationToken Token { get; private set; }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            History.Add(messages.ToArray());
            Assert.Equal("test-model", options?.ModelId);
            foreach (var item in response(History.Count, History[^1]))
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSessionFactory : IMcpToolSessionFactory
    {
        public List<FakeSession> Sessions { get; } = [];
        public bool FailInvocation { get; init; }
        public bool HasSchema { get; init; } = true;
        public Func<string, IDictionary<string, object?>?, object?>? OnInvoke { get; init; }
        public Task<IMcpToolSession> ConnectAsync(CancellationToken cancellationToken)
        {
            var session = new FakeSession { FailInvocation = FailInvocation, HasSchema = HasSchema, OnInvoke = OnInvoke };
            lock (Sessions) Sessions.Add(session);
            return Task.FromResult<IMcpToolSession>(session);
        }
    }

    private sealed class FakeSession : IMcpToolSession
    {
        public bool HasSchema { get; init; } = true;
        public IReadOnlyList<AITool> Tools => HasSchema
            ? [AIFunctionFactory.Create(() => "schema", "get_database_schema"),
                AIFunctionFactory.Create((string sql) => sql, "execute_read_query")]
            : [];
        public string? InvokedName { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailInvocation { get; init; }
        public Func<string, IDictionary<string, object?>?, object?>? OnInvoke { get; init; }
        public int Calls { get; private set; }
        public Task<object?> InvokeAsync(string name, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
        {
            Calls++;
            InvokedName = name;
            if (FailInvocation) throw new AssistantException(502, "mcp_tool_failure", "A query failed.");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OnInvoke is null ? "INTERNAL_SCHEMA_DATA" : OnInvoke(name, arguments));
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
