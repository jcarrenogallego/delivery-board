using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using Microsoft.Extensions.Options;

namespace DeliveryBoard.Assistant.Endpoints;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        foreach (var provider in new[] { "ollama", "openai" })
        {
            app.MapPost($"/api/assistant/{provider}/chat", (ChatRequest request, IChatService service,
                IOptions<AssistantOptions> options) => Handle(provider, request, service, options.Value))
                .WithName($"{provider}Chat")
                .WithTags("Assistant")
                .WithSummary($"Stream a conversation using {provider} and Delivery Board MCP")
                .WithDescription("Send user/assistant history ending in user. SSE events contain JSON: "
                    + "delta {text}, done {provider,model}, or terminal error {code,message,traceId}. "
                    + "Tool calls/results are private. OpenAI receives MCP results needed for the answer. "
                    + "Use fetch streaming or curl -N; EventSource does not support POST.")
                .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status502BadGateway)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
                .ProducesProblem(StatusCodes.Status504GatewayTimeout);
        }
    }

    private static IResult Handle(string provider, ChatRequest request, IChatService service, AssistantOptions options)
    {
        if (request.Messages is not { Count: > 0 } messages
            || messages.Any(message => message is null || message.Role is not ("user" or "assistant")
                || string.IsNullOrWhiteSpace(message.Content))
            || messages[^1]!.Role != "user")
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["messages"] = ["Provide non-empty user/assistant messages ending in a user message."]
            });

        var history = messages.Select(message => new ConversationMessage(message!.Role!, message.Content!)).ToArray();
        return new ChatStreamResult(provider, history, service, options);
    }
}
