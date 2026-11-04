namespace DeliveryBoard.Application.WorkItems;

public interface IDeleteWorkItemUseCase
{
    Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken);
}
