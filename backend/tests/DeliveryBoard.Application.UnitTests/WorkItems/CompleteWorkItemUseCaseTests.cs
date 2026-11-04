using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class CompleteWorkItemUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WithExistingWorkItem_CompletesAndSavesIt()
    {
        var workItem = CreateWorkItem();
        var repository = new FakeWorkItemRepository(workItem);
        var useCase = new CompleteWorkItemUseCase(repository);

        var result = await useCase.ExecuteAsync(workItem.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithInProgressWorkItem_CompletesAndSavesIt()
    {
        var workItem = CreateWorkItem();
        workItem.Start();
        var repository = new FakeWorkItemRepository(workItem);

        var result = await new CompleteWorkItemUseCase(repository).ExecuteAsync(workItem.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownId_ReturnsNullWithoutSaving()
    {
        var repository = new FakeWorkItemRepository();
        var useCase = new CompleteWorkItemUseCase(repository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithCompletedWorkItem_ReturnsItWithoutSavingAgain()
    {
        var workItem = CreateWorkItem();
        workItem.Complete();
        var repository = new FakeWorkItemRepository(workItem);
        var useCase = new CompleteWorkItemUseCase(repository);

        var result = await useCase.ExecuteAsync(workItem.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithSaveConflict_PropagatesWithoutRetrying()
    {
        var item = CreateWorkItem();
        var conflict = new WorkItemConflictException("La tarea ha cambiado.");
        var repository = new FakeWorkItemRepository(item) { SaveFailure = conflict };
        var useCase = new CompleteWorkItemUseCase(repository);

        var error = await Assert.ThrowsAsync<WorkItemConflictException>(() =>
            useCase.ExecuteAsync(item.Id, CancellationToken.None));

        Assert.Same(conflict, error);
        Assert.Equal(1, repository.SaveCount);
    }

    private static WorkItem CreateWorkItem() =>
        WorkItem.Create("Preparar entrega", "Ana", WorkItemPriority.High, DateTimeOffset.UtcNow);

    private sealed class FakeWorkItemRepository(params WorkItem[] items) : IWorkItemRepository
    {
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public int SaveCount { get; private set; }
        public Exception? SaveFailure { get; init; }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(WorkItem workItem, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        }
    }
}
