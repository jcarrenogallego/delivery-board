using System.Net;
using System.Net.Http.Json;
using System.Text;
using DeliveryBoard.Api.Endpoints;
using DeliveryBoard.Api.Errors;
using DeliveryBoard.Application.Assistant;
using DeliveryBoard.Infrastructure.Assistant;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DeliveryBoard.Api.Tests;

public sealed class AssistantQueryTests
{
    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    public async Task QueryAggregatesFragmentedUnicodeAndSendsSingleTrimmedUserMessage(string provider)
    {
        var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal($"/api/assistant/{provider}/chat", request.RequestUri!.AbsolutePath);
            Assert.Equal("text/event-stream", request.Headers.Accept.Single().MediaType);
            var body = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(token);
            var messages = body.GetProperty("messages");
            Assert.Equal(1, messages.GetArrayLength());
            Assert.Equal("user", messages[0].GetProperty("role").GetString());
            Assert.Equal("¿Pendientes?", messages[0].GetProperty("content").GetString());
            return Sse("event: delta\r\ndata: {\"text\":\"¡Sí! 📝\\n\"}\r\n\r\n" +
                "event: delta\r\ndata: {\"text\":\"Dos tareas.\"}\r\n\r\n" + Done(provider));
        });
        await using var app = await StartApp(handler);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/assistant/query", new { provider, question = "  ¿Pendientes?  " });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AssistantQueryResponse>();
        Assert.Equal(new AssistantQueryResponse("¡Sí! 📝\nDos tareas.", provider, "test-model"), result);
    }

    [Theory]
    [InlineData(null, "question")]
    [InlineData("other", "question")]
    [InlineData("OpenAI", "question")]
    [InlineData("ollama", null)]
    [InlineData("openai", " \r\n ")]
    public async Task InvalidInputReturns400WithoutCallingAssistant(string? provider, string? question)
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Must not call provider"));
        await using var app = await StartApp(handler);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/assistant/query", new { provider, question });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task MalformedRequestReturns400(string body)
    {
        await using var app = await StartApp(new StubHandler((_, _) => throw new InvalidOperationException()));
        using var response = await app.GetTestClient().PostAsync("/api/assistant/query", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("event: delta\ndata: {\"text\":\"partial\"}\n\n")]
    [InlineData("event: delta\ndata: {bad}\n\n")]
    [InlineData("event: delta\ndata: {\"text\":5}\n\n")]
    [InlineData("event: done\ndata: {\"provider\":\"ollama\",\"model\":\"model\"}\n\n")]
    [InlineData("event: delta\ndata: {\"text\":\" \"}\n\nevent: done\ndata: {\"provider\":\"ollama\",\"model\":\"model\"}\n\n")]
    [InlineData("event: delta\ndata: {\"text\":\"partial\"}\n\nevent: done\ndata: {\"provider\":\"openai\",\"model\":\"model\"}\n\n")]
    [InlineData("event: delta\ndata: {\"text\":\"partial\"}\n\nevent: done\ndata: {\"provider\":\"ollama\",\"model\":\" \"}\n\n")]
    [InlineData("event: delta\ndata: []\n\n")]
    [InlineData("event: unknown\ndata: {}\n\n")]
    public async Task InvalidStreamReturns502WithoutPartialText(string events)
    {
        await using var app = await StartApp(new StubHandler((_, _) => Task.FromResult(Sse(events))));
        using var response = await Query(app);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("partial", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("configuration_missing", 503)]
    [InlineData("provider_unavailable", 503)]
    [InlineData("mcp_unavailable", 503)]
    [InlineData("request_timeout", 504)]
    [InlineData("tool_failed", 502)]
    public async Task ErrorEventMapsSafeProblemDetails(string code, int expectedStatus)
    {
        var events = "event: delta\ndata: {\"text\":\"partial-secret\"}\n\n" +
            $"event: error\ndata: {{\"code\":\"{code}\",\"message\":\"secret upstream exception\"}}\n\n";
        await using var app = await StartApp(new StubHandler((_, _) => Task.FromResult(Sse(events))));
        using var response = await Query(app);
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", body);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(503, 503)]
    [InlineData(504, 504)]
    [InlineData(401, 502)]
    [InlineData(500, 502)]
    public async Task UpstreamHttpStatusMapsWithoutLeakingBody(int upstreamStatus, int expectedStatus)
    {
        await using var app = await StartApp(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)upstreamStatus)
        { Content = new StringContent("secret") })));
        using var response = await Query(app);
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WrongContentTypeReturns502()
    {
        await using var app = await StartApp(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("not SSE") })));
        using var response = await Query(app);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task UnreachableAssistantReturns503()
    {
        await using var app = await StartApp(new StubHandler((_, _) => throw new HttpRequestException("secret")));
        using var response = await Query(app);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("not a URL", 210)]
    [InlineData("file:///C:/private", 210)]
    [InlineData("http://localhost", 0)]
    [InlineData("http://localhost", int.MaxValue)]
    public async Task InvalidConfigurationFailsOnlyOnQuery(string url, int seconds)
    {
        var client = new AssistantQueryHttpClient(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException())),
            Options.Create(new AssistantClientOptions { BaseUrl = url, TimeoutSeconds = seconds }));
        var error = await Assert.ThrowsAsync<AssistantQueryException>(() => client.QueryAsync("ollama", "question", default));
        Assert.Equal(AssistantQueryFailure.Unavailable, error.Failure);
    }

    [Fact]
    public async Task TimeoutIncludesStreamingBodyAfterHeaders()
    {
        var blocked = new BlockingStream();
        var client = CreateClient(new StubHandler((_, _) => Task.FromResult(Sse(blocked))), 1);
        var error = await Assert.ThrowsAsync<AssistantQueryException>(() => client.QueryAsync("ollama", "question", default));
        Assert.Equal(AssistantQueryFailure.Timeout, error.Failure);
        Assert.True(blocked.Cancelled);
    }

    [Fact]
    public async Task CallerCancellationStopsStreamingWithoutBecomingTimeout()
    {
        var blocked = new BlockingStream();
        var client = CreateClient(new StubHandler((_, _) => Task.FromResult(Sse(blocked))));
        using var cancellation = new CancellationTokenSource();
        var query = client.QueryAsync("ollama", "question", cancellation.Token);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.True(blocked.Cancelled);
    }

    [Fact]
    public async Task TimeoutIncludesWaitingForHeaders()
    {
        var client = CreateClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        }), 1);
        var error = await Assert.ThrowsAsync<AssistantQueryException>(() => client.QueryAsync("ollama", "question", default));
        Assert.Equal(AssistantQueryFailure.Timeout, error.Failure);
    }

    [Fact]
    public async Task ConcurrentQueriesDoNotShareAnswers()
    {
        var client = CreateClient(new StubHandler(async (request, token) =>
        {
            var json = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(token);
            var question = json.GetProperty("messages")[0].GetProperty("content").GetString();
            await Task.Yield();
            return Sse($"event: delta\ndata: {{\"text\":\"{question}\"}}\n\n" + Done("ollama"));
        }));
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => client.QueryAsync("ollama", $"question-{i}", default)));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => $"question-{i}"), results.Select(result => result.Answer));
    }

    [Fact]
    public async Task OpenApiDocumentsQueryContractAndFailures()
    {
        await using var app = await StartApp(new StubHandler((_, _) => throw new InvalidOperationException()));
        using var response = await app.GetTestClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var endpoint = document.GetProperty("paths").GetProperty("/api/assistant/query").GetProperty("post");
        var statuses = endpoint.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "502", "503", "504" })
        {
            Assert.True(statuses.TryGetProperty(status, out _));
        }
        Assert.True(statuses.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
        var schema = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schema.GetProperty("AssistantQueryRequest").GetProperty("properties").TryGetProperty("question", out _));
        Assert.True(schema.GetProperty("AssistantQueryResponse").GetProperty("properties").TryGetProperty("answer", out _));
    }

    private static AssistantQueryHttpClient CreateClient(StubHandler handler, int seconds = 210) =>
        new(new HttpClient(handler), Options.Create(new AssistantClientOptions { TimeoutSeconds = seconds }));

    private static Task<HttpResponseMessage> Query(WebApplication app) =>
        app.GetTestClient().PostAsJsonAsync("/api/assistant/query", new { provider = "ollama", question = "question" });

    private static async Task<WebApplication> StartApp(StubHandler handler)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddAssistantQueryServices(new ConfigurationBuilder().Build());
        builder.Services.AddHttpClient<IAssistantQueryClient, AssistantQueryHttpClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapAssistantEndpoints();
        app.MapOpenApi();
        await app.StartAsync();
        return app;
    }

    private static string Done(string provider) => $"event: done\ndata: {{\"provider\":\"{provider}\",\"model\":\"test-model\"}}\n\n";

    private static HttpResponseMessage Sse(string events) => Sse(new FragmentedStream(Encoding.UTF8.GetBytes(events)));

    private static HttpResponseMessage Sse(Stream stream) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(stream) { Headers = { ContentType = new("text/event-stream") } }
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
