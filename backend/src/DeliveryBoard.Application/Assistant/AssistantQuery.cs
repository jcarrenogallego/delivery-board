namespace DeliveryBoard.Application.Assistant;

/// <summary>A standalone question for a configured assistant provider.</summary>
public sealed record AssistantQueryRequest(string? Provider, string? Question);

/// <summary>The complete natural-language answer from the assistant.</summary>
public sealed record AssistantQueryResponse(string Answer, string Provider, string Model);

public interface IAssistantQueryClient
{
    Task<AssistantQueryResponse> QueryAsync(string provider, string question, CancellationToken cancellationToken);
}

public interface IQueryAssistantUseCase
{
    Task<AssistantQueryResponse> ExecuteAsync(AssistantQueryRequest request, CancellationToken cancellationToken);
}

public sealed class QueryAssistantUseCase(IAssistantQueryClient client) : IQueryAssistantUseCase
{
    public Task<AssistantQueryResponse> ExecuteAsync(AssistantQueryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Provider is not ("ollama" or "openai") || string.IsNullOrWhiteSpace(request.Question))
        {
            throw new AssistantQueryException(AssistantQueryFailure.InvalidRequest);
        }

        return client.QueryAsync(request.Provider, request.Question.Trim(), cancellationToken);
    }
}

public enum AssistantQueryFailure { InvalidRequest, Unavailable, Timeout, InvalidResponse }

public sealed class AssistantQueryException(AssistantQueryFailure failure) : Exception(failure switch
{
    AssistantQueryFailure.InvalidRequest => "Indica un proveedor válido (ollama u openai) y una pregunta no vacía.",
    AssistantQueryFailure.Unavailable => "El asistente o el proveedor seleccionado no está disponible. Comprueba su configuración.",
    AssistantQueryFailure.Timeout => "El asistente ha superado el tiempo de espera. Inténtalo de nuevo.",
    _ => "No se pudo obtener una respuesta completa del asistente. Inténtalo de nuevo."
})
{
    public AssistantQueryFailure Failure { get; } = failure;
}
