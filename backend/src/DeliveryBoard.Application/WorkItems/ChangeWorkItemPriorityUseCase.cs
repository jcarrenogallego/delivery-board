using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class ChangeWorkItemPriorityUseCase(IWorkItemRepository repository) : IChangeWorkItemPriorityUseCase
{
    public async Task<WorkItemResponse?> ExecuteAsync(
        Guid id, ChangeWorkItemPriorityRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var priority = WorkItemPriorityParser.Parse(request.Priority);
        var saved = await repository.ChangePriorityAsync(id, priority, cancellationToken);
        return saved?.ToResponse();
    }
}
