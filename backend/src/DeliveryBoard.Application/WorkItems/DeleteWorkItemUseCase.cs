using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class DeleteWorkItemUseCase(IWorkItemRepository repository) : IDeleteWorkItemUseCase
{
    public Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, cancellationToken);
}
