using System.Diagnostics;
using DeliveryBoard.Assistant.Application;

namespace DeliveryBoard.Assistant.Errors;

internal sealed record FailureResponse(int Status, string Code, string Message, McpFailureDiagnostic? Mcp = null)
{
    public static string GetTraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    public static FailureResponse From(Exception exception) => exception switch
    {
        AssistantException error => new(error.StatusCode, error.Code, error.SafeMessage, error.McpDiagnostic),
        BadHttpRequestException error => new(error.StatusCode, "invalid_request", "The request body is invalid."),
        OperationCanceledException => new(504, "request_timeout", "The conversation exceeded its time limit."),
        _ => new(502, "assistant_error", "The assistant could not complete the conversation.")
    };

    public IResult ToProblem(string traceId)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = Code, ["traceId"] = traceId };
        if (Mcp is not null) extensions["mcp"] = Mcp;
        return TypedResults.Problem(statusCode: Status, title: Message, extensions: extensions);
    }

    public void LogSafeDiagnostic(HttpContext context)
    {
        if (Mcp is null) return;
        // Do not pass the exception, tool arguments, schema, or returned database content to the logger.
        context.RequestServices.GetRequiredService<ILogger<FailureResponse>>().LogWarning(
            new EventId(2001, "McpToolFailure"),
            "MCP tool {McpTool} failed with {McpErrorCode} ({McpFailureKind}); traceId {TraceId}",
            Mcp.Tool, Mcp.ErrorCode, Mcp.Kind, GetTraceId(context));
    }
}
