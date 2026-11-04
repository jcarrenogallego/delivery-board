using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class StartWorkItemUseCaseTests
{
    [Fact]
    public void Start_WithPendingItem_ChangesOnlyStatusAndRepeatsWithoutChanges()
    {
        var item = CreateItem();
        var original = Snapshot(item);

        Assert.True(item.Start());
        Assert.Equal(WorkItemStatus.InProgress, item.Status);
        Assert.Equal(original, Snapshot(item));
        Assert.False(item.Start());
        Assert.Equal(original, Snapshot(item));
        Assert.Equal(WorkItemStatus.InProgress, item.Status);
    }

    [Fact]
    public void Start_WithCompletedItem_RejectsWithoutChangingFields()
    {
        var item = CreateItem();
        item.Complete();
        var original = Snapshot(item);

        Assert.Throws<WorkItemConflictException>(() => item.Start());

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(WorkItemStatus.Completed, item.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WithPendingItem_ReturnsConfirmedItemAndSavesOnce()
    {
        var item = CreateItem();
        var original = Snapshot(item);
        var repository = new FakeRepository(item);
        var result = await new StartWorkItemUseCase(repository).ExecuteAsync(item.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Changed);
        Assert.Equal(item.Id, result.WorkItem.Id);
        Assert.Equal("InProgress", result.WorkItem.Status);
        Assert.Equal(item.Title, result.WorkItem.Title);
        Assert.Equal(item.Owner, result.WorkItem.Owner);
        Assert.Equal("High", result.WorkItem.Priority);
        Assert.Equal(item.CreatedAt, result.WorkItem.CreatedAt);
        Assert.Equal(original, Snapshot(item));
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(1, repository.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithInProgressItem_ReturnsUnchangedWithoutSavingOrCheckingTokens()
    {
        var item = CreateItem();
        item.Start();
        var repository = new FakeRepository(item) { SaveFailure = new WorkItemConflictException("Obsoleto") };

        var result = await new StartWorkItemUseCase(repository).ExecuteAsync(item.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.Changed);
        Assert.Equal("InProgress", result.WorkItem.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(1, repository.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithCompletedItem_RejectsWithoutSaving()
    {
        var item = CreateItem();
        item.Complete();
        var repository = new FakeRepository(item);

        await Assert.ThrowsAsync<WorkItemConflictException>(() =>
            new StartWorkItemUseCase(repository).ExecuteAsync(item.Id, CancellationToken.None));

        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(WorkItemStatus.Completed, item.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingItem_ReturnsNullWithoutSaving()
    {
        var repository = new FakeRepository();
        var result = await new StartWorkItemUseCase(repository).ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(1, repository.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithSaveConflict_PropagatesWithoutRetryOrReload()
    {
        var item = CreateItem();
        var conflict = new WorkItemConflictException("La tarea ha cambiado o ya no existe.");
        var repository = new FakeRepository(item) { SaveFailure = conflict };

        var error = await Assert.ThrowsAsync<WorkItemConflictException>(() =>
            new StartWorkItemUseCase(repository).ExecuteAsync(item.Id, CancellationToken.None));

        Assert.Same(conflict, error);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(1, repository.ReadCount);
    }

    private static WorkItem CreateItem() => WorkItem.Create("Preparar entrega", "Ana", WorkItemPriority.High,
        new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero));

    private static (Guid, string, string, WorkItemPriority, DateTimeOffset) Snapshot(WorkItem item) =>
        (item.Id, item.Title, item.Owner, item.Priority, item.CreatedAt);

    private sealed class FakeRepository(params WorkItem[] items) : IWorkItemRepository
    {
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public int SaveCount { get; private set; }
        public int ReadCount { get; private set; }
        public Exception? SaveFailure { get; init; }
        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(items.SingleOrDefault(item => item.Id == id));
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        }
        public Task AddAsync(WorkItem workItem, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
