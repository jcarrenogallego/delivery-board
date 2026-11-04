using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DeliveryBoard.Infrastructure.Persistence;

internal sealed class WorkItemRepository(DeliveryBoardDbContext dbContext) : IWorkItemRepository
{
    public async Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken)
    {
        var changed = await dbContext.WorkItems.Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Comment, comment == string.Empty ? null : comment), cancellationToken);
        if (changed == 0) return null;
        return await dbContext.WorkItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken)
    {
        // Always write, even when an older tracked snapshot has the requested value.
        var changed = await dbContext.WorkItems.Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Priority, priority), cancellationToken);
        if (changed == 0) return null;
        return await dbContext.WorkItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<T> WithDependencyLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Small board: serialize graph writes, starts and deletes. Ordinary reads remain available.
        // The lock also conflicts with other writes, so validation and persistence share one graph.
        await dbContext.Database.ExecuteSqlRawAsync("LOCK TABLE work_items IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        WithDependencyLockAsync(async () =>
        {
            if (!await dbContext.WorkItems.AnyAsync(item => item.Id == id, cancellationToken)) return false;
            if (await dbContext.WorkItems.AnyAsync(item => item.PrerequisiteIds.Contains(id), cancellationToken))
                throw new WorkItemConflictException("No se puede eliminar una tarea que es prerrequisito de otra. Retira primero la dependencia.");
            return await dbContext.WorkItems.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken) == 1;
        }, cancellationToken);

    public async Task AddAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        dbContext.WorkItems.Add(workItem);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.WorkItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.WorkItems
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new WorkItemConflictException(
                "La tarea ha cambiado o ya no existe. Actualiza el tablero antes de volver a intentarlo.",
                exception);
        }
    }
}
