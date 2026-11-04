namespace DeliveryBoard.Application.WorkItems;

public interface ICreateWorkItemUseCase
{
    Task<WorkItemResponse> ExecuteAsync(
        CreateWorkItemRequest request,
        CancellationToken cancellationToken);
}
