using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class ReassignWorkItemOwnerUseCase(IWorkItemRepository repository) : IReassignWorkItemOwnerUseCase
{
    public async Task<WorkItemResponse?> ExecuteAsync(
        Guid id,
        ReassignWorkItemOwnerRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workItem = await repository.GetByIdAsync(id, cancellationToken);
        if (workItem is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (workItem.ReassignOwner(request.Owner, request.ExpectedOwner))
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return workItem.ToResponse();
    }
}
