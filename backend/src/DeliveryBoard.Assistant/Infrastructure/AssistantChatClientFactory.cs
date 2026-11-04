using System.ClientModel;
using System.ClientModel.Primitives;
using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace DeliveryBoard.Assistant.Infrastructure;

public sealed class AssistantChatClientFactory(
    IOptions<OllamaOptions> ollama,
    IOptions<OpenAIOptions> openAI) : IAssistantChatClientFactory
{
    public ChatProviderMetadata GetProvider(string provider)
    {
        switch (provider)
        {
            case "ollama":
                Require(ollama.Value.Model, "The Ollama model is not configured.");
                if (!Uri.TryCreate(ollama.Value.BaseUrl, UriKind.Absolute, out var uri)
                    || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new AssistantException(503, "configuration_missing", "The Ollama address is invalid.");
                return new(provider, ollama.Value.Model!);
            case "openai":
                Require(openAI.Value.Model, "The OpenAI model is not configured.");
                Require(openAI.Value.ApiKey, "The OpenAI API key is not configured.");
                return new(provider, openAI.Value.Model!);
            default:
                throw new AssistantException(400, "invalid_provider", "The requested provider is not supported.");
        }
    }

    public IChatClient Create(string provider)
    {
        var metadata = GetProvider(provider);
        if (provider == "ollama")
        {
            // The shared request deadline owns cancellation, including slow local model loads.
            var http = new HttpClient { BaseAddress = new Uri(ollama.Value.BaseUrl), Timeout = Timeout.InfiniteTimeSpan };
            return new OwnedHttpChatClient(new OllamaApiClient(http, metadata.Model), http);
        }
        // A request is never retried automatically, especially after streaming has begun.
        var client = new OpenAIClient(new ApiKeyCredential(openAI.Value.ApiKey!),
            new OpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(0), NetworkTimeout = Timeout.InfiniteTimeSpan });
        return new OpenAIChatClient(client.GetChatClient(metadata.Model).AsIChatClient());
    }

    private static void Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new AssistantException(503, "configuration_missing", message);
    }

    private sealed class OwnedHttpChatClient(IChatClient client, HttpClient http) : DelegatingChatClient(client)
    {
        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally { if (disposing) http.Dispose(); }
        }
    }

    private sealed class OpenAIChatClient(IChatClient client) : DelegatingChatClient(client)
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var stream = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (await MoveNextAsync(stream)) yield return stream.Current;
        }

        private static async ValueTask<bool> MoveNextAsync(IAsyncEnumerator<ChatResponseUpdate> stream)
        {
            try { return await stream.MoveNextAsync(); }
            catch (ClientResultException exception) when (exception.Status == 0)
            {
                throw new AssistantException(503, "provider_unavailable", "The model provider is unavailable.", exception);
            }
        }
    }
}
