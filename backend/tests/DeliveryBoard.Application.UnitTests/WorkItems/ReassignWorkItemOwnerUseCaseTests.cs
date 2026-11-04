using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class ReassignWorkItemOwnerUseCaseTests
{
    [Theory]
    [InlineData(WorkItemStatus.Pending)]
    [InlineData(WorkItemStatus.InProgress)]
    public async Task ExecuteAsync_WithEditableWorkItem_ChangesOnlyOwner(WorkItemStatus status)
    {
        var item = CreateWorkItem(status);
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.ExecuteAsync(
            item.Id, new(" Luis ", " Ana "), cancellation.Token);

        Assert.Equal(original with { Owner = "Luis" }, result);
        Assert.Equal(original with { Owner = "Luis" }, Snapshot(item));
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(item.Id, repository.ReceivedId);
        Assert.Equal(cancellation.Token, repository.ReadToken);
        Assert.Equal(cancellation.Token, repository.SaveToken);
    }

    [Theory]
    [InlineData(null, "Ana")]
    [InlineData("", "Ana")]
    [InlineData(" \t ", "Ana")]
    [InlineData("Luis", null)]
    [InlineData("Luis", "")]
    [InlineData("Luis", " \t ")]
    public async Task ExecuteAsync_WithMissingOwnerInput_RejectsWithoutChanges(string? owner, string? expectedOwner)
    {
        var item = CreateWorkItem();
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        await Assert.ThrowsAsync<BusinessRuleException>(() => useCase.ExecuteAsync(
            item.Id, new(owner, expectedOwner), CancellationToken.None));

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WithOwnerInputOver80Characters_RejectsWithoutChanges(bool invalidNewOwner)
    {
        var item = CreateWorkItem();
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);
        var tooLong = new string('A', 81);
        var request = invalidNewOwner
            ? new ReassignWorkItemOwnerRequest(tooLong, "Ana")
            : new ReassignWorkItemOwnerRequest("Luis", tooLong);

        await Assert.ThrowsAsync<BusinessRuleException>(() => useCase.ExecuteAsync(
            item.Id, request, CancellationToken.None));

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_With80NormalizedCharacters_AcceptsBothInputs()
    {
        var previousOwner = new string('A', 80);
        var newOwner = new string('B', 80);
        var item = WorkItem.Create("Preparar entrega", previousOwner, WorkItemPriority.High, DateTimeOffset.UtcNow);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        var result = await useCase.ExecuteAsync(
            item.Id, new($" {newOwner} ", $" {previousOwner} "), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(newOwner, result.Owner);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnchangedNormalizedOwner_ReturnsItemWithoutSaving()
    {
        var item = CreateWorkItem();
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        var result = await useCase.ExecuteAsync(item.Id, new(" Ana ", " Ana "), CancellationToken.None);

        Assert.Equal(original, result);
        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentCasing_ChangesOwner()
    {
        var item = CreateWorkItem();
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        var result = await useCase.ExecuteAsync(item.Id, new("ANA", "Ana"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ANA", result.Owner);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory]
    [InlineData("Luis", "Marta")]
    [InlineData("Ana", "Marta")]
    [InlineData("Luis", "ana")]
    public async Task ExecuteAsync_WithStaleExpectedOwner_ConflictsEvenForNoOp(string owner, string expectedOwner)
    {
        var item = CreateWorkItem();
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        await Assert.ThrowsAsync<WorkItemConflictException>(() => useCase.ExecuteAsync(
            item.Id, new(owner, expectedOwner), CancellationToken.None));

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData("Luis")]
    [InlineData("Ana")]
    public async Task ExecuteAsync_WithCompletedWorkItem_ConflictsEvenForNoOp(string owner)
    {
        var item = CreateWorkItem();
        item.Complete();
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        await Assert.ThrowsAsync<WorkItemConflictException>(() => useCase.ExecuteAsync(
            item.Id, new(owner, "Ana"), CancellationToken.None));

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownId_ReturnsNullWithoutSaving()
    {
        var repository = new FakeWorkItemRepository();
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), new("Luis", "Ana"), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithSaveConflict_PropagatesWithoutRetrying()
    {
        var item = CreateWorkItem();
        var conflict = new WorkItemConflictException("La tarea ha cambiado.");
        var repository = new FakeWorkItemRepository(item) { SaveFailure = conflict };
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        var error = await Assert.ThrowsAsync<WorkItemConflictException>(() => useCase.ExecuteAsync(
            item.Id, new("Luis", "Ana"), CancellationToken.None));

        Assert.Same(conflict, error);
        Assert.Equal(1, repository.ReadCount);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancelledToken_DoesNotReadOrMutate()
    {
        var item = CreateWorkItem();
        var original = Snapshot(item);
        var repository = new FakeWorkItemRepository(item);
        var useCase = new ReassignWorkItemOwnerUseCase(repository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(
            item.Id, new("Luis", "Ana"), cancellation.Token));

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.ReadCount);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledAfterRead_DoesNotMutateOrSave()
    {
        var item = CreateWorkItem();
        var original = Snapshot(item);
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeWorkItemRepository(item) { AfterRead = cancellation.Cancel };
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(
            item.Id, new("Luis", "Ana"), cancellation.Token));

        Assert.Equal(original, Snapshot(item));
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSaveIsCancelled_PropagatesWithoutRetrying()
    {
        var item = CreateWorkItem();
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeWorkItemRepository(item) { BeforeSave = cancellation.Cancel };
        var useCase = new ReassignWorkItemOwnerUseCase(repository);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(
            item.Id, new("Luis", "Ana"), cancellation.Token));

        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(1, repository.ReadCount);
    }

    private static WorkItem CreateWorkItem(WorkItemStatus status = WorkItemStatus.Pending)
    {
        var item = WorkItem.Create("Preparar entrega", "Ana", WorkItemPriority.High, DateTimeOffset.UtcNow);
        // Simulate hydration of an existing InProgress task; no workflow transition is added.
        typeof(WorkItem).GetProperty(nameof(WorkItem.Status))!.SetValue(item, status);
        return item;
    }

    private static WorkItemResponse Snapshot(WorkItem item) =>
        new(item.Id, item.Title, item.Owner, item.Priority.ToString(), item.Status.ToString(), item.CreatedAt);

    private sealed class FakeWorkItemRepository(params WorkItem[] items) : IWorkItemRepository
    {
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public int ReadCount { get; private set; }
        public int SaveCount { get; private set; }
        public Guid ReceivedId { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public CancellationToken SaveToken { get; private set; }
        public Exception? SaveFailure { get; init; }
        public Action? AfterRead { get; init; }
        public Action? BeforeSave { get; init; }

        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            ReceivedId = id;
            ReadToken = cancellationToken;
            var item = items.SingleOrDefault(item => item.Id == id);
            AfterRead?.Invoke();
            return Task.FromResult(item);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            SaveToken = cancellationToken;
            BeforeSave?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(WorkItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
