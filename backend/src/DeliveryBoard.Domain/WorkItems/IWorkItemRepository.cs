namespace DeliveryBoard.Domain.WorkItems;

public interface IWorkItemRepository
{
    // Implementations serialize graph mutations with start/delete to keep dependencies valid.
    Task<T> WithDependencyLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

    Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken);

    Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(WorkItem workItem, CancellationToken cancellationToken);

    Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
