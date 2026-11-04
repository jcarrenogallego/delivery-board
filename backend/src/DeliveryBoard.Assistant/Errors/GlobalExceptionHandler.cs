using Microsoft.AspNetCore.Diagnostics;

namespace DeliveryBoard.Assistant.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested) return true;
        var failure = FailureResponse.From(exception);
        failure.LogSafeDiagnostic(context);
        await failure.ToProblem(FailureResponse.GetTraceId(context)).ExecuteAsync(context);
        return true;
    }
}
