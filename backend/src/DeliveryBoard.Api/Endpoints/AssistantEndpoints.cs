using DeliveryBoard.Application.Assistant;

namespace DeliveryBoard.Api.Endpoints;

public static class AssistantEndpoints
{
    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/assistant/query", async (
            AssistantQueryRequest request,
            IQueryAssistantUseCase useCase,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await useCase.ExecuteAsync(request, cancellationToken)))
            .WithTags("Assistant")
            .WithName("QueryAssistant")
            .WithSummary("Consulta al asistente y obtiene una respuesta completa")
            .WithDescription("Selecciona ollama u openai. Cada pregunta es independiente; la respuesta reúne los fragmentos del asistente en texto natural.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
        return endpoints;
    }
}
