using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public interface IChangeWorkItemDependenciesUseCase
{
    Task<WorkItemResponse?> ExecuteAsync(Guid id, ChangeWorkItemDependenciesRequest request, CancellationToken cancellationToken);
}

public sealed class ChangeWorkItemDependenciesUseCase(IWorkItemRepository repository) : IChangeWorkItemDependenciesUseCase
{
    public Task<WorkItemResponse?> ExecuteAsync(Guid id, ChangeWorkItemDependenciesRequest request, CancellationToken cancellationToken) =>
        repository.WithDependencyLockAsync<WorkItemResponse?>(async () =>
        {
            var item = await repository.GetByIdAsync(id, cancellationToken);
            if (item is null) return null;
            if (item.Status != WorkItemStatus.Pending)
                throw new WorkItemConflictException("Solo las tareas pendientes pueden cambiar sus dependencias.");
            var ids = request.PrerequisiteIds ?? throw new InvalidWorkItemDependencyException("Indica las dependencias; usa [] para quitarlas.");
            if (ids.Contains(id) || ids.Distinct().Count() != ids.Length)
                throw new InvalidWorkItemDependencyException("No se permiten dependencias propias ni duplicadas.");
            var items = await repository.GetAllAsync(cancellationToken);
            var graph = items.ToDictionary(candidate => candidate.Id);
            if (ids.Any(dependency => !graph.ContainsKey(dependency))) return null;
            var visited = new HashSet<Guid>();
            bool ReachesTarget(Guid candidate)
            {
                if (candidate == id) return true;
                return visited.Add(candidate) && graph[candidate].PrerequisiteIds.Any(ReachesTarget);
            }
            if (ids.Any(ReachesTarget))
                throw new InvalidWorkItemDependencyException("La dependencia crearía un ciclo entre tareas.");
            item.SetPrerequisites(ids);
            await repository.SaveChangesAsync(cancellationToken);
            return item.ToResponse();
        }, cancellationToken);
}
