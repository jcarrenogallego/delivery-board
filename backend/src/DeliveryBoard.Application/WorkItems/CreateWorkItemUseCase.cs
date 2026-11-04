using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class CreateWorkItemUseCase(IWorkItemRepository repository) : ICreateWorkItemUseCase
{
    public async Task<WorkItemResponse> ExecuteAsync(
        CreateWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var priority = WorkItemPriorityParser.Parse(request.Priority, defaultWhenMissing: true);

        var workItem = WorkItem.Create(request.Title, request.Owner, priority, DateTimeOffset.UtcNow, request.Comment);
        
        await repository.AddAsync(workItem, cancellationToken);

        return workItem.ToResponse();
    }
}
