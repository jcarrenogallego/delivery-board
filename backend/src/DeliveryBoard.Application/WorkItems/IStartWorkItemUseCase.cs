namespace DeliveryBoard.Application.WorkItems;

public interface IStartWorkItemUseCase
{
    Task<StartWorkItemResponse?> ExecuteAsync(Guid id, CancellationToken cancellationToken);
}
