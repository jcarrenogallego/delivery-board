using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class CompleteWorkItemUseCase(IWorkItemRepository repository) : ICompleteWorkItemUseCase
{
    public async Task<WorkItemResponse?> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var workItem = await repository.GetByIdAsync(id, cancellationToken);
        if (workItem is null)
        {
            return null;
        }

        if (workItem.Complete())
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return workItem.ToResponse();
    }
}
