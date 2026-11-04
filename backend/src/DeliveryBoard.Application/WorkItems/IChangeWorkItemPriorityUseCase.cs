namespace DeliveryBoard.Application.WorkItems;

public interface IChangeWorkItemPriorityUseCase
{
    Task<WorkItemResponse?> ExecuteAsync(Guid id, ChangeWorkItemPriorityRequest request, CancellationToken cancellationToken);
}
