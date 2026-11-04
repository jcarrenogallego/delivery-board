using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Application.Assistant;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryBoard.Api.Errors;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isExpectedError = exception is AssistantQueryException or BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } or BusinessRuleException or WorkItemConflictException or InvalidWorkItemPriorityException or InvalidWorkItemDependencyException;
        var statusCode = exception switch
        {
            AssistantQueryException { Failure: AssistantQueryFailure.InvalidRequest } => StatusCodes.Status400BadRequest,
            AssistantQueryException { Failure: AssistantQueryFailure.Unavailable } => StatusCodes.Status503ServiceUnavailable,
            AssistantQueryException { Failure: AssistantQueryFailure.Timeout } => StatusCodes.Status504GatewayTimeout,
            AssistantQueryException => StatusCodes.Status502BadGateway,
            BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } => StatusCodes.Status400BadRequest,
            InvalidWorkItemDependencyException => StatusCodes.Status400BadRequest,
            InvalidWorkItemPriorityException => StatusCodes.Status400BadRequest,
            BusinessRuleException => StatusCodes.Status422UnprocessableEntity,
            WorkItemConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        if (!isExpectedError)
        {
            logger.LogError(exception, "Error inesperado al procesar la solicitud.");
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = isExpectedError ? "No se pudo completar la operación" : "Error interno",
                Detail = exception is BadHttpRequestException ? "El cuerpo de la solicitud no es válido."
                    : isExpectedError ? exception.Message : "Se produjo un error inesperado."
            }
        });
    }
}
