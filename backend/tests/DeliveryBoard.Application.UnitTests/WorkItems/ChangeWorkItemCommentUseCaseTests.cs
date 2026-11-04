using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class ChangeWorkItemCommentUseCaseTests
{
    public static IEnumerable<object?[]> Changes()
    {
        foreach (var status in Enum.GetValues<WorkItemStatus>())
        foreach (var original in new string?[] { null, "Anterior" })
        foreach (var comment in new string?[] { null, "", "  ", "  Nuevo 🧪\n<script>literal</script>  " })
            yield return [status, original, comment];
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task ExecuteAsync_AllStates_ChangesOnlyComment(WorkItemStatus status, string? original, string? comment)
    {
        var item = WorkItem.Create("Demo", "Ana", WorkItemPriority.High, DateTimeOffset.UnixEpoch, original);
        var other = WorkItem.Create("Otra", "Bea", WorkItemPriority.Low, DateTimeOffset.UnixEpoch, "Intacto");
        item.SetPrerequisites([other.Id]);
        if (status == WorkItemStatus.InProgress) item.Start();
        if (status == WorkItemStatus.Completed) item.Complete();
        var before = (item.Id, item.Title, item.Owner, item.Priority, item.Status, item.CreatedAt);
        var repo = new FakeRepository(item);
        using var cts = new CancellationTokenSource();
        var result = await new ChangeWorkItemCommentUseCase(repo).ExecuteAsync(item.Id, new(comment), cts.Token);
        Assert.Equal(comment == "" ? null : comment, result!.Comment);
        Assert.Equal(result.Comment, item.Comment);
        Assert.Equal(before, (item.Id, item.Title, item.Owner, item.Priority, item.Status, item.CreatedAt));
        Assert.Equal(new[] { other.Id }, item.PrerequisiteIds);
        Assert.Equal("Intacto", other.Comment);
        Assert.Equal(cts.Token, repo.Token);
        Assert.Equal(1, repo.Writes);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_ReturnsNull()
    {
        Assert.Null(await new ChangeWorkItemCommentUseCase(new FakeRepository()).ExecuteAsync(
            Guid.NewGuid(), new("Low"), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_DoesNotWrite()
    {
        var repo = new FakeRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ChangeWorkItemCommentUseCase(repo).ExecuteAsync(Guid.NewGuid(), new("Low"), new CancellationToken(true)));
        Assert.Equal(0, repo.Writes);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryFailure_PropagatesWithoutRetry()
    {
        var repo = new FakeRepository { Failure = new InvalidOperationException("Unavailable") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ChangeWorkItemCommentUseCase(repo).ExecuteAsync(Guid.NewGuid(), new("Low"), CancellationToken.None));
        Assert.Same(repo.Failure, error);
        Assert.Equal(1, repo.Writes);
    }

    private sealed class FakeRepository(WorkItem? item = null) : IWorkItemRepository
    {
        public int Writes { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }
        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            Token = cancellationToken;
            if (Failure is not null) return Task.FromException<WorkItem?>(Failure);
            if (item?.Id != id) return Task.FromResult<WorkItem?>(null);
            item.ChangeComment(comment);
            return Task.FromResult<WorkItem?>(item);
        }
        public Task AddAsync(WorkItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
