using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class ChangeWorkItemPriorityUseCaseTests
{
    [Theory]
    [InlineData(WorkItemStatus.Pending, "Low")]
    [InlineData(WorkItemStatus.Pending, "Medium")]
    [InlineData(WorkItemStatus.Pending, "High")]
    [InlineData(WorkItemStatus.InProgress, "Low")]
    [InlineData(WorkItemStatus.InProgress, "Medium")]
    [InlineData(WorkItemStatus.InProgress, "High")]
    [InlineData(WorkItemStatus.Completed, "Low")]
    [InlineData(WorkItemStatus.Completed, "Medium")]
    [InlineData(WorkItemStatus.Completed, "High")]
    public async Task ExecuteAsync_AllStates_ChangesOnlyPriority(WorkItemStatus status, string priority)
    {
        var item = WorkItem.Create("Demo", "Ana", WorkItemPriority.Medium, DateTimeOffset.UtcNow);
        if (status == WorkItemStatus.InProgress) item.Start();
        if (status == WorkItemStatus.Completed) item.Complete();
        var before = (item.Id, item.Title, item.Owner, item.Status, item.CreatedAt);
        var repo = new FakeRepository(item);
        var token = CancellationToken.None;
        var result = await new ChangeWorkItemPriorityUseCase(repo).ExecuteAsync(item.Id, new(priority), token);
        Assert.Equal(priority, result!.Priority);
        Assert.Equal(before, (item.Id, item.Title, item.Owner, item.Status, item.CreatedAt));
        Assert.Equal(1, repo.Writes);
        Assert.Equal(token, repo.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Urgent")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("Low, High")]
    public async Task ExecuteAsync_Invalid_DoesNotWrite(string? priority)
    {
        var repo = new FakeRepository();
        await Assert.ThrowsAsync<InvalidWorkItemPriorityException>(() =>
            new ChangeWorkItemPriorityUseCase(repo).ExecuteAsync(Guid.NewGuid(), new(priority), CancellationToken.None));
        Assert.Equal(0, repo.Writes);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_ReturnsNull()
    {
        Assert.Null(await new ChangeWorkItemPriorityUseCase(new FakeRepository()).ExecuteAsync(
            Guid.NewGuid(), new("Low"), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_DoesNotWrite()
    {
        var repo = new FakeRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ChangeWorkItemPriorityUseCase(repo).ExecuteAsync(Guid.NewGuid(), new("Low"), new CancellationToken(true)));
        Assert.Equal(0, repo.Writes);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryFailure_PropagatesWithoutRetry()
    {
        var repo = new FakeRepository { Failure = new InvalidOperationException("Unavailable") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ChangeWorkItemPriorityUseCase(repo).ExecuteAsync(Guid.NewGuid(), new("Low"), CancellationToken.None));
        Assert.Same(repo.Failure, error);
        Assert.Equal(1, repo.Writes);
    }

    [Fact]
    public void Domain_UndefinedEnum_IsRejected()
    {
        var item = WorkItem.Create("Demo", "Ana", WorkItemPriority.Low, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidWorkItemPriorityException>(() => item.ChangePriority((WorkItemPriority)3));
        Assert.Equal(WorkItemPriority.Low, item.Priority);
        Assert.Throws<InvalidWorkItemPriorityException>(() => WorkItem.Create("Demo", "Ana", (WorkItemPriority)3, DateTimeOffset.UtcNow));
    }

    private sealed class FakeRepository(WorkItem? item = null) : IWorkItemRepository
    {
        public int Writes { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            Token = cancellationToken;
            if (Failure is not null) return Task.FromException<WorkItem?>(Failure);
            if (item?.Id != id) return Task.FromResult<WorkItem?>(null);
            item.ChangePriority(priority);
            return Task.FromResult<WorkItem?>(item);
        }
        public Task AddAsync(WorkItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
