using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class StartWorkItemUseCase(IWorkItemRepository repository) : IStartWorkItemUseCase
{
    public Task<StartWorkItemResponse?> ExecuteAsync(Guid id, CancellationToken cancellationToken) =>
        repository.WithDependencyLockAsync<StartWorkItemResponse?>(async () =>
        {
            var workItem = await repository.GetByIdAsync(id, cancellationToken);
            if (workItem is null) return null;

            if (workItem.Status == WorkItemStatus.Pending && workItem.PrerequisiteIds.Length > 0)
            {
                var items = await repository.GetAllAsync(cancellationToken);
                var byId = items.ToDictionary(item => item.Id);
                var unmet = workItem.PrerequisiteIds
                    .Where(dependency => !byId.TryGetValue(dependency, out var item) || item.Status != WorkItemStatus.Completed)
                    .Select(dependency => byId.TryGetValue(dependency, out var item) ? item.Title : dependency.ToString())
                    .ToArray();
                if (unmet.Length > 0)
                    throw new WorkItemConflictException("No se puede iniciar: faltan prerrequisitos por completar: " + string.Join(", ", unmet));
            }
            var changed = workItem.Start();
            if (changed) await repository.SaveChangesAsync(cancellationToken);
            return new StartWorkItemResponse(workItem.ToResponse(), changed);
        }, cancellationToken);
}
