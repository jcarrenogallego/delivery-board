using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using DeliveryBoard.Application.Assistant;
using Microsoft.Extensions.Options;

namespace DeliveryBoard.Infrastructure.Assistant;

public sealed class AssistantQueryHttpClient(HttpClient client, IOptions<AssistantClientOptions> options) : IAssistantQueryClient
{
    public async Task<AssistantQueryResponse> QueryAsync(string provider, string question, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.TimeoutSeconds <= 0 || settings.TimeoutSeconds > (uint.MaxValue - 1) / 1000 ||
            !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            throw new AssistantQueryException(AssistantQueryFailure.Unavailable);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"/api/assistant/{provider}/chat"))
            {
                Content = JsonContent.Create(new { messages = new[] { new { role = "user", content = question } } })
            };
            request.Headers.Accept.ParseAdd("text/event-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new AssistantQueryException(response.StatusCode switch
                {
                    HttpStatusCode.ServiceUnavailable => AssistantQueryFailure.Unavailable,
                    HttpStatusCode.GatewayTimeout => AssistantQueryFailure.Timeout,
                    _ => AssistantQueryFailure.InvalidResponse
                });
            }

            if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            {
                throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var answer = new StringBuilder();
            await foreach (var item in SseParser.Create(stream).EnumerateAsync(deadline.Token))
            {
                using var document = JsonDocument.Parse(item.Data);
                var data = document.RootElement;
                switch (item.EventType)
                {
                    case "delta":
                        answer.Append(ReadString(data, "text"));
                        break;
                    case "done":
                        var model = ReadString(data, "model");
                        if (ReadString(data, "provider") != provider || string.IsNullOrWhiteSpace(model) ||
                            string.IsNullOrWhiteSpace(answer.ToString()))
                        {
                            throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
                        }

                        return new AssistantQueryResponse(answer.ToString(), provider, model);
                    case "error":
                        throw new AssistantQueryException(ReadString(data, "code") switch
                        {
                            "configuration_missing" or "provider_unavailable" or "mcp_unavailable" => AssistantQueryFailure.Unavailable,
                            "request_timeout" => AssistantQueryFailure.Timeout,
                            _ => AssistantQueryFailure.InvalidResponse
                        });
                    default:
                        throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
                }
            }

            throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AssistantQueryException(AssistantQueryFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new AssistantQueryException(AssistantQueryFailure.Unavailable);
        }
        catch (IOException)
        {
            throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
        }
        catch (JsonException)
        {
            throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
        }
    }

    private static string ReadString(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new AssistantQueryException(AssistantQueryFailure.InvalidResponse);
        }

        return value.GetString()!;
    }
}
