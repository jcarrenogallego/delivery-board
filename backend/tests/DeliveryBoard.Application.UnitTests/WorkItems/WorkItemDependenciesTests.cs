using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class WorkItemDependenciesTests
{
    [Fact]
    public async Task MultiplePrerequisites_AreStoredAndCanBeRemoved()
    {
        var target = Item("Objetivo"); var first = Item("API"); var second = Item("UI");
        var repository = new Repository(target, first, second);
        var useCase = new ChangeWorkItemDependenciesUseCase(repository);
        var result = await useCase.ExecuteAsync(target.Id, new([first.Id, second.Id]), default);
        Assert.Equal(new[] { first.Id, second.Id }, result!.PrerequisiteIds);
        Assert.Equal(new[] { first.Id, second.Id }, target.PrerequisiteIds);
        await useCase.ExecuteAsync(target.Id, new([]), default);
        Assert.Empty(target.PrerequisiteIds);
        Assert.Equal(2, repository.Saves);
        Assert.Equal(2, repository.Locks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelfOrDuplicate_RejectsWithoutChangingPreviousDependencies(bool duplicate)
    {
        var target = Item("Objetivo"); var prerequisite = Item("API");
        target.SetPrerequisites([prerequisite.Id]);
        var repository = new Repository(target, prerequisite);
        await Assert.ThrowsAsync<InvalidWorkItemDependencyException>(() => new ChangeWorkItemDependenciesUseCase(repository)
            .ExecuteAsync(target.Id, new(duplicate ? [prerequisite.Id, prerequisite.Id] : [target.Id]), default));
        Assert.Equal(new[] { prerequisite.Id }, target.PrerequisiteIds);
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task IndirectCycle_RejectsWithoutSaving()
    {
        var a = Item("A"); var b = Item("B"); var c = Item("C");
        a.SetPrerequisites([b.Id]); b.SetPrerequisites([c.Id]);
        var repository = new Repository(a, b, c);
        await Assert.ThrowsAsync<InvalidWorkItemDependencyException>(() => new ChangeWorkItemDependenciesUseCase(repository)
            .ExecuteAsync(c.Id, new([a.Id]), default));
        Assert.Empty(c.PrerequisiteIds);
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task SharedPrerequisiteWithoutCycle_IsAllowed()
    {
        var a = Item("A"); var b = Item("B"); var c = Item("C"); var target = Item("T");
        a.SetPrerequisites([c.Id]); b.SetPrerequisites([c.Id]);
        var result = await new ChangeWorkItemDependenciesUseCase(new Repository(a, b, c, target))
            .ExecuteAsync(target.Id, new([a.Id, b.Id]), default);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTargetOrPrerequisite_ReturnsNullWithoutSaving(bool missingTarget)
    {
        var target = Item("T"); var repository = new Repository(target);
        var result = await new ChangeWorkItemDependenciesUseCase(repository)
            .ExecuteAsync(missingTarget ? Guid.NewGuid() : target.Id, new([Guid.NewGuid()]), default);
        Assert.Null(result);
        Assert.Empty(target.PrerequisiteIds);
        Assert.Equal(0, repository.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonPending_RejectsDependencyEdits(bool completed)
    {
        var target = Item("T");
        if (completed) target.Complete(); else target.Start();
        var repository = new Repository(target);
        await Assert.ThrowsAsync<WorkItemConflictException>(() => new ChangeWorkItemDependenciesUseCase(repository)
            .ExecuteAsync(target.Id, new([]), default));
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task MissingDependencyList_IsInvalidRatherThanClearingDependencies()
    {
        var target = Item("T");
        await Assert.ThrowsAsync<InvalidWorkItemDependencyException>(() => new ChangeWorkItemDependenciesUseCase(new Repository(target))
            .ExecuteAsync(target.Id, new(null), default));
    }

    [Fact]
    public async Task Blocked_IsSubsetOfPending_AndCompletionUnblocksImmediately()
    {
        var target = Item("T"); var a = Item("A"); var b = Item("B"); target.SetPrerequisites([a.Id, b.Id]);
        var repository = new Repository(target, a, b);
        var dashboard = new GetDashboardUseCase(repository);
        var before = await dashboard.ExecuteAsync(default);
        Assert.Equal(3, before.Pending); Assert.Equal(1, before.Blocked); Assert.Equal(3, before.Total);
        Assert.True(before.Items.Single(item => item.Id == target.Id).IsBlocked);
        var error = await Assert.ThrowsAsync<WorkItemConflictException>(() => new StartWorkItemUseCase(repository).ExecuteAsync(target.Id, default));
        Assert.Contains("A", error.Message); Assert.Contains("B", error.Message);
        Assert.Equal(WorkItemStatus.Pending, target.Status); Assert.Equal(0, repository.Saves);
        a.Complete();
        await Assert.ThrowsAsync<WorkItemConflictException>(() => new StartWorkItemUseCase(repository).ExecuteAsync(target.Id, default));
        b.Complete();
        var after = await dashboard.ExecuteAsync(default);
        Assert.Equal(1, after.Pending); Assert.Equal(2, after.Completed); Assert.Equal(0, after.Blocked);
        Assert.False(after.Items.Single(item => item.Id == target.Id).IsBlocked);
        var started = await new StartWorkItemUseCase(repository).ExecuteAsync(target.Id, default);
        Assert.True(started!.Changed); Assert.Equal("InProgress", started.WorkItem.Status);
    }

    [Fact]
    public async Task ExistingInProgress_IsIdempotentEvenIfPrerequisitesAreUnmet()
    {
        var target = Item("T"); var a = Item("A"); target.SetPrerequisites([a.Id]); target.Start();
        var repository = new Repository(target, a);
        var result = await new StartWorkItemUseCase(repository).ExecuteAsync(target.Id, default);
        Assert.False(result!.Changed); Assert.Equal(0, repository.Saves);
        var dashboard = await new GetDashboardUseCase(repository).ExecuteAsync(default);
        Assert.Equal(0, dashboard.Blocked); Assert.Equal(1, dashboard.InProgress);
    }

    private static WorkItem Item(string title) => WorkItem.Create(title, "Ana", WorkItemPriority.Medium, DateTimeOffset.UtcNow);

    private sealed class Repository(params WorkItem[] items) : IWorkItemRepository
    {
        public int Saves { get; private set; }
        public int Locks { get; private set; }
        public Task<T> WithDependencyLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) { Locks++; return operation(); }
        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(items.SingleOrDefault(item => item.Id == id));
        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<WorkItem>>(items);
        public Task SaveChangesAsync(CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
        public Task AddAsync(WorkItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
