namespace DeliveryBoard.Application.WorkItems;

public interface ICompleteWorkItemUseCase
{
    Task<WorkItemResponse?> ExecuteAsync(Guid id, CancellationToken cancellationToken);
}
