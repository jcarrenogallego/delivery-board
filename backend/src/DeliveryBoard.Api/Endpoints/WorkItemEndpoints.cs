using DeliveryBoard.Application.WorkItems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DeliveryBoard.Api.Endpoints;

public static class WorkItemEndpoints
{
    public static IEndpointRouteBuilder MapWorkItemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/work-items").WithTags("Work items");

        group.MapGet("/dashboard", async (
            IGetDashboardUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var dashboard = await useCase.ExecuteAsync(cancellationToken);
            return Results.Ok(dashboard);
        }).WithName("GetDashboard");

        group.MapPost("/", async (
            CreateWorkItemRequest request,
            ICreateWorkItemUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var workItem = await useCase.ExecuteAsync(request, cancellationToken);
            return Results.Created($"/api/work-items/{workItem.Id}", workItem);
        }).WithName("CreateWorkItem");

        group.MapPatch("/{id:guid}/start", async (
            Guid id,
            IStartWorkItemUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("StartWorkItem");

        group.MapPatch("/{id:guid}/complete", async (
            Guid id,
            ICompleteWorkItemUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var workItem = await useCase.ExecuteAsync(id, cancellationToken);
            return workItem is null ? Results.NotFound() : Results.Ok(workItem);
        }).WithName("CompleteWorkItem");

        group.MapPatch("/{id:guid}/owner", async (
            Guid id,
            ReassignWorkItemOwnerRequest request,
            IReassignWorkItemOwnerUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var workItem = await useCase.ExecuteAsync(id, request, cancellationToken);
            return workItem is null ? Results.NotFound() : Results.Ok(workItem);
        }).WithName("ReassignWorkItemOwner");

        group.MapPatch("/{id:guid}/dependencies", async (
            Guid id,
            ChangeWorkItemDependenciesRequest request,
            IChangeWorkItemDependenciesUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var workItem = await useCase.ExecuteAsync(id, request, cancellationToken);
            return workItem is null ? Results.NotFound() : Results.Ok(workItem);
        }).WithName("ChangeWorkItemDependencies");

        group.MapPatch("/{id:guid}/priority", async (
            Guid id,
            ChangeWorkItemPriorityRequest request,
            IChangeWorkItemPriorityUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var workItem = await useCase.ExecuteAsync(id, request, cancellationToken);
            return workItem is null ? Results.NotFound() : Results.Ok(workItem);
        }).WithName("ChangeWorkItemPriority");

        group.MapPatch("/{id:guid}/comment", async Task<Results<Ok<WorkItemResponse>, NotFound>> (
            Guid id,
            ChangeWorkItemCommentRequest request,
            IChangeWorkItemCommentUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var workItem = await useCase.ExecuteAsync(id, request, cancellationToken);
            return workItem is null ? TypedResults.NotFound() : TypedResults.Ok(workItem);
        }).WithName("ChangeWorkItemComment")
            .WithSummary("Replace or remove a task comment")
            .WithDescription("Available in every state. Null, empty or omitted comment removes the current text.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDeleteWorkItemUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var deleted = await useCase.ExecuteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).WithName("DeleteWorkItem");

        return endpoints;
    }
}
