namespace DeliveryBoard.Application.WorkItems;

public interface IReassignWorkItemOwnerUseCase
{
    Task<WorkItemResponse?> ExecuteAsync(
        Guid id,
        ReassignWorkItemOwnerRequest request,
        CancellationToken cancellationToken);
}
