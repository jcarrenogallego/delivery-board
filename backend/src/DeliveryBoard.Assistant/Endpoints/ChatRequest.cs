using System.Text.Json.Serialization;
using DeliveryBoard.Assistant.Application;

namespace DeliveryBoard.Assistant.Endpoints;

/// <summary>A stateless conversation ending in a user message.</summary>
public sealed record ChatRequest(IReadOnlyList<ChatMessageRequest?>? Messages);

/// <summary>A public user or assistant message; system and tool messages are server-owned.</summary>
public sealed record ChatMessageRequest(string? Role, string? Content);

/// <summary>A streamed fragment of public assistant text.</summary>
public sealed record ChatDelta(string Text);

/// <summary>Successful completion of a streamed conversation.</summary>
public sealed record ChatDone(string Provider, string Model);

/// <summary>A sanitized terminal streaming failure.</summary>
public sealed record ChatError(string Code, string Message, string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] McpFailureDiagnostic? Mcp = null);
