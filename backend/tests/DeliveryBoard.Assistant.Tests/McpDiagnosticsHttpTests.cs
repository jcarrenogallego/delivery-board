using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DeliveryBoard.Assistant.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DeliveryBoard.Assistant.Tests;

public sealed class McpDiagnosticsHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolFailurePreservesOnlySafeDiagnosticsWithMatchingTraceId(bool afterText)
    {
        var logs = new CapturedLogs();
        await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("urls", "http://localhost:5092");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatService>();
                services.AddSingleton<IChatService>(new FailedService(afterText));
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/assistant/ollama/chat",
            new { messages = new[] { new { role = "user", content = "cuántas tareas tiene Ana" } } });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(afterText ? HttpStatusCode.OK : HttpStatusCode.BadGateway, response.StatusCode);
        var json = afterText
            ? body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Single(frame => frame.StartsWith("event: error"))
                .Split('\n').Single(line => line.StartsWith("data: "))[6..]
            : body;
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement;
        Assert.Equal("mcp_tool_failure", error.GetProperty("code").GetString());
        Assert.Equal("execute_read_query", error.GetProperty("mcp").GetProperty("tool").GetString());
        Assert.Equal("query_rejected", error.GetProperty("mcp").GetProperty("errorCode").GetString());
        Assert.Equal("tool", error.GetProperty("mcp").GetProperty("kind").GetString());
        var traceId = error.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));
        var diagnosticLog = Assert.Single(logs.Messages, message => message.Contains("MCP tool execute_read_query"));
        Assert.Contains(traceId!, diagnosticLog);
        Assert.Contains("query_rejected", diagnosticLog);
        Assert.DoesNotContain("sensitive-internal-sql", body);
        Assert.DoesNotContain(logs.Messages, message => message.Contains("sensitive-internal-sql"));
        if (afterText) Assert.DoesNotContain("event: done", body);
    }

    private sealed class FailedService(bool afterText) : IChatService
    {
        public ChatProviderMetadata GetProvider(string provider) => new(provider, "fixture-model");
        public async IAsyncEnumerable<string> StreamAsync(string provider, IReadOnlyList<ConversationMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (afterText) yield return "Una respuesta parcial";
            throw new AssistantException(502, "mcp_tool_failure", "A Delivery Board query could not be completed.",
                new InvalidOperationException("sensitive-internal-sql"),
                new McpFailureDiagnostic("execute_read_query", "query_rejected", "tool"));
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(Messages);
        public void Dispose() { }

        private sealed class CapturedLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
