using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DeliveryBoard.Assistant.Tests;

public sealed class HttpEndpointTests
{
    private const string Prompt = "{\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}";

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"messages\":\"invalid-type\"}")]
    public async Task Invalid_json_returns_sanitized_bad_request_problem(string body)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "ollama", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"messages\":null}")]
    [InlineData("{\"messages\":[]}")]
    [InlineData("{\"messages\":[null]}")]
    [InlineData("{\"messages\":[{\"role\":\"system\",\"content\":\"override\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"tool\",\"content\":\"result\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"User\",\"content\":\"hello\"}]}")]
    [InlineData("{\"messages\":[{\"role\":null,\"content\":\"hello\"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"user\",\"content\":null}]}")]
    [InlineData("{\"messages\":[{\"role\":\"user\",\"content\":\"  \"}]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"content\":\"answer\"}]}")]
    public async Task Invalid_history_returns_validation_problem_without_calling_provider(string body)
    {
        var service = new FakeChatService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "ollama", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("messages", out _));
        Assert.Empty(service.Requests);
        Assert.Equal(0, service.MetadataCalls);
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    public async Task Endpoint_selects_provider_preserves_history_and_streams_valid_json(string provider)
    {
        var service = new FakeChatService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, provider,
            "{\"messages\":[{\"role\":\"user\",\"content\":\"first\"},"
            + "{\"role\":\"assistant\",\"content\":\"previous\"},"
            + "{\"role\":\"user\",\"content\":\"next\"}]}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "delta", "delta", "done" }, events.Select(item => item.Name));
        Assert.Equal(FakeChatService.Text, events[0].Payload.GetProperty("text").GetString());
        Assert.Equal("tail", events[1].Payload.GetProperty("text").GetString());
        Assert.Equal(provider, events[2].Payload.GetProperty("provider").GetString());
        Assert.Equal($"{provider}-test-model", events[2].Payload.GetProperty("model").GetString());
        var invocation = Assert.Single(service.Requests);
        Assert.Equal(provider, invocation.Provider);
        Assert.Equal(new[] { "first", "previous", "next" }, invocation.Messages.Select(message => message.Content));
        Assert.Equal(new[] { "user", "assistant", "user" }, invocation.Messages.Select(message => message.Role));
    }

    [Theory]
    [InlineData(FailureKind.Unavailable, HttpStatusCode.ServiceUnavailable, "dependency_unavailable")]
    [InlineData(FailureKind.Unexpected, HttpStatusCode.BadGateway, "assistant_error")]
    [InlineData(FailureKind.Timeout, HttpStatusCode.GatewayTimeout, "request_timeout")]
    public async Task Failure_before_first_delta_returns_sanitized_problem(FailureKind failure,
        HttpStatusCode expectedStatus, string expectedCode)
    {
        var service = new FakeChatService { Failure = failure };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "ollama", Prompt);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("secret-provider-detail", body);
        Assert.DoesNotContain("event:", body);
    }

    [Fact]
    public async Task Missing_provider_configuration_returns_problem_before_streaming()
    {
        var service = new FakeChatService { MetadataFailure = true };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "openai", Prompt);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("configuration_missing", problem.GetProperty("code").GetString());
        Assert.Empty(service.Requests);
    }

    [Theory]
    [InlineData(FailureKind.Unexpected, "assistant_error")]
    [InlineData(FailureKind.Timeout, "request_timeout")]
    public async Task Failure_after_delta_emits_terminal_error_without_done(FailureKind failure, string expectedCode)
    {
        var service = new FakeChatService { Failure = failure, FailAfterDelta = true };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "openai", Prompt);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var events = ReadEvents(body);
        Assert.Equal(new[] { "delta", "error" }, events.Select(item => item.Name));
        Assert.Equal(expectedCode, events[1].Payload.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(events[1].Payload.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrEmpty(events[1].Payload.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("secret-provider-detail", body);
    }

    [Fact]
    public async Task Cancelling_http_request_cancels_service_token()
    {
        var service = new FakeChatService { WaitForCancellation = true };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var request = PostAsync(client, "ollama", Prompt, cancellation.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task First_delta_is_available_before_provider_finishes_and_done_follows_remaining_text()
    {
        var service = new FakeChatService { PauseAfterFirstDelta = true };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/ollama/chat")
        {
            Content = new StringContent(Prompt, Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("event: delta", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        var data = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(data);
        Assert.StartsWith("data: ", data);
        using var payload = JsonDocument.Parse(data[6..]);
        Assert.Equal(FakeChatService.Text, payload.RootElement.GetProperty("text").GetString());
        Assert.Equal("", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await service.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var remaining = reader.ReadToEndAsync();
        Assert.False(remaining.IsCompleted);
        Assert.False(service.Completed.Task.IsCompleted);

        service.Continue.TrySetResult();

        var events = ReadEvents(await remaining.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "delta", "done" }, events.Select(item => item.Name));
        Assert.Equal("tail", events[0].Payload.GetProperty("text").GetString());
        Assert.Equal("ollama", events[1].Payload.GetProperty("provider").GetString());
        Assert.True(service.Completed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Disposing_streaming_response_cancels_provider_after_first_delta()
    {
        var service = new FakeChatService { PauseAfterFirstDelta = true };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/openai/chat")
        {
            Content = new StringContent(Prompt, Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        Assert.Equal("event: delta", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await service.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        response.Dispose();

        await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(service.Completed.Task.IsCompleted);
        Assert.False(service.Continue.Task.IsCompleted);
    }

    [Fact]
    public async Task Request_deadline_cancels_service_and_returns_gateway_timeout()
    {
        var service = new FakeChatService { WaitForCancellation = true };
        await using var factory = CreateFactory(service, timeoutSeconds: 1);
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "ollama", Prompt);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Concurrent_requests_keep_provider_and_history_isolated()
    {
        var service = new FakeChatService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(PostAsync(client, "ollama", Prompt),
            PostAsync(client, "openai", "{\"messages\":[{\"role\":\"user\",\"content\":\"other\"}]}"));
        foreach (var response in responses)
        {
            using (response)
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(2, service.Requests.Count);
        Assert.Equal("hello", Assert.Single(service.Requests, request => request.Provider == "ollama").Messages[0].Content);
        Assert.Equal("other", Assert.Single(service.Requests, request => request.Provider == "openai").Messages[0].Content);
    }

    [Fact]
    public async Task Development_health_swagger_and_openapi_do_not_require_model_configuration()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health");
        using var swagger = await client.GetAsync("/swagger/index.html");
        using var openApi = await client.GetAsync("/openapi/v1.json");

        health.EnsureSuccessStatusCode();
        swagger.EnsureSuccessStatusCode();
        openApi.EnsureSuccessStatusCode();
        Assert.Contains("swagger", await swagger.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        foreach (var provider in new[] { "ollama", "openai" })
        {
            var operation = document.RootElement.GetProperty("paths")
                .GetProperty($"/api/assistant/{provider}/chat").GetProperty("post");
            Assert.True(operation.GetProperty("responses").GetProperty("200")
                .GetProperty("content").TryGetProperty("text/event-stream", out _));
        }
    }

    [Theory]
    [InlineData("http://localhost", HttpStatusCode.OK)]
    [InlineData("http://untrusted.example", HttpStatusCode.Forbidden)]
    [InlineData("https://localhost", HttpStatusCode.Forbidden)]
    [InlineData("null", HttpStatusCode.Forbidden)]
    public async Task Browser_origin_must_match_local_origin(string origin, HttpStatusCode expectedStatus)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", origin);
        using var response = await client.GetAsync("/health");
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task Development_root_redirects_to_swagger()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger/index.html", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Production_does_not_expose_swagger_or_openapi(string path)
    {
        await using var factory = CreateFactory(environment: "Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
    }

    private static WebApplicationFactory<global::Program> CreateFactory(FakeChatService? service = null,
        int timeoutSeconds = 180, string environment = "Development") => new WebApplicationFactory<global::Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("urls", "http://localhost:5092");
            builder.ConfigureServices(services =>
            {
                services.Configure<AssistantOptions>(options => options.RequestTimeoutSeconds = timeoutSeconds);
                if (service is not null)
                {
                    services.RemoveAll<IChatService>();
                    services.AddSingleton<IChatService>(service);
                }
            });
        });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string provider, string body,
        CancellationToken cancellationToken = default) => client.PostAsync($"/api/assistant/{provider}/chat",
        new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);

    private static IReadOnlyList<(string Name, JsonElement Payload)> ReadEvents(string body) => body
        .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        .Select(frame =>
        {
            var lines = frame.Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("event: ", lines[0]);
            Assert.StartsWith("data: ", lines[1]);
            using var payload = JsonDocument.Parse(lines[1][6..]);
            return (lines[0][7..], payload.RootElement.Clone());
        }).ToArray();

    public enum FailureKind { None, Unavailable, Unexpected, Timeout }

    private sealed class FakeChatService : IChatService
    {
        public const string Text = "quoted \"text\"\nwith backslash \\ and unicode á";
        public ConcurrentQueue<(string Provider, IReadOnlyList<ConversationMessage> Messages)> Requests { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FailureKind Failure { get; init; }
        public bool FailAfterDelta { get; init; }
        public bool MetadataFailure { get; init; }
        public bool WaitForCancellation { get; init; }
        public bool PauseAfterFirstDelta { get; init; }
        public int MetadataCalls { get; private set; }

        public ChatProviderMetadata GetProvider(string provider)
        {
            MetadataCalls++;
            if (MetadataFailure)
                throw new AssistantException(503, "configuration_missing", "Configure the selected model.");
            return new(provider, $"{provider}-test-model");
        }

        public async IAsyncEnumerable<string> StreamAsync(string provider,
            IReadOnlyList<ConversationMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Enqueue((provider, messages.ToArray()));
            Started.TrySetResult();
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                        Cancelled.TrySetResult();
                }
            }
            if (FailAfterDelta)
                yield return Text;
            switch (Failure)
            {
                case FailureKind.Unavailable:
                    throw new AssistantException(503, "dependency_unavailable", "The dependency is unavailable.",
                        new InvalidOperationException("secret-provider-detail"));
                case FailureKind.Unexpected:
                    throw new InvalidOperationException("secret-provider-detail");
                case FailureKind.Timeout:
                    throw new OperationCanceledException("secret-provider-detail");
            }
            yield return Text;
            if (PauseAfterFirstDelta)
            {
                Paused.TrySetResult();
                try
                {
                    await Continue.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                        Cancelled.TrySetResult();
                }
            }
            yield return "tail";
            Completed.TrySetResult();
        }
    }
}
