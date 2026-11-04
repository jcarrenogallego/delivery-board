using Microsoft.Extensions.AI;

namespace DeliveryBoard.Assistant.Application;

public sealed record ConversationMessage(string Role, string Content);
public sealed record ChatProviderMetadata(string Provider, string Model);
public sealed record McpFailureDiagnostic(string Tool, string ErrorCode, string Kind);

public interface IChatService
{
    ChatProviderMetadata GetProvider(string provider);
    IAsyncEnumerable<string> StreamAsync(string provider, IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default);
}

public interface IAssistantChatClientFactory
{
    ChatProviderMetadata GetProvider(string provider);
    IChatClient Create(string provider);
}

public interface IMcpToolSessionFactory
{
    Task<IMcpToolSession> ConnectAsync(CancellationToken cancellationToken);
}

public interface IMcpToolSession : IAsyncDisposable
{
    IReadOnlyList<AITool> Tools { get; }
    Task<object?> InvokeAsync(string name, IDictionary<string, object?>? arguments, CancellationToken cancellationToken);
}

public sealed class AssistantException(int statusCode, string code, string safeMessage, Exception? innerException = null,
    McpFailureDiagnostic? mcpDiagnostic = null)
    : Exception(safeMessage, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string SafeMessage { get; } = safeMessage;
    public McpFailureDiagnostic? McpDiagnostic { get; } = mcpDiagnostic;
}
