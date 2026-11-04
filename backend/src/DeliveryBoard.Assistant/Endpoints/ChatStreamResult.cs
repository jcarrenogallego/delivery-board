using System.Text.Json;
using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using DeliveryBoard.Assistant.Errors;
using Microsoft.AspNetCore.Http.Features;

namespace DeliveryBoard.Assistant.Endpoints;

internal sealed class ChatStreamResult(string provider, IReadOnlyList<ConversationMessage> messages,
    IChatService service, AssistantOptions options) : IResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(HttpContext context)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            if (options.RequestTimeoutSeconds <= 0)
                throw new AssistantException(503, "configuration_missing", "A positive request timeout is required.");
            deadline.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            var metadata = service.GetProvider(provider);
            await using var stream = service.StreamAsync(provider, messages, deadline.Token).GetAsyncEnumerator(deadline.Token);
            // Keep the response unstarted until the first model/MCP operation has succeeded.
            var hasNext = await stream.MoveNextAsync();
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            while (hasNext)
            {
                if (!string.IsNullOrEmpty(stream.Current))
                    await WriteEventAsync(context, "delta", new ChatDelta(stream.Current), deadline.Token);
                hasNext = await stream.MoveNextAsync();
            }
            deadline.Token.ThrowIfCancellationRequested();
            await WriteEventAsync(context, "done", new ChatDone(metadata.Provider, metadata.Model), deadline.Token);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            var failure = FailureResponse.From(exception);
            failure.LogSafeDiagnostic(context);
            if (!context.Response.HasStarted)
            {
                context.Response.ContentType = null;
                await failure.ToProblem(FailureResponse.GetTraceId(context)).ExecuteAsync(context);
            }
            else
            {
                // The expired deadline must not prevent writing a terminal timeout event.
                await WriteEventAsync(context, "error", new ChatError(failure.Code, failure.Message,
                    FailureResponse.GetTraceId(context), failure.Mcp), context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // A disconnected client cannot receive an error event.
        }
    }

    private static async Task WriteEventAsync<T>(HttpContext context, string name, T payload, CancellationToken token)
    {
        await context.Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n", token);
        await context.Response.Body.FlushAsync(token);
    }
}
