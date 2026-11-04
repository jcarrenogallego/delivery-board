using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class DeleteWorkItemUseCaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_ReturnsDeletionResultAndPassesIdAndToken(bool deleted)
    {
        var repository = new FakeWorkItemRepository(deleted);
        var useCase = new DeleteWorkItemUseCase(repository);
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.ExecuteAsync(id, cancellation.Token);

        Assert.Equal(deleted, result);
        Assert.Equal(id, repository.DeletedId);
        Assert.Equal(cancellation.Token, repository.ReceivedToken);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancelledToken_PropagatesCancellation()
    {
        var repository = new FakeWorkItemRepository(true);
        var useCase = new DeleteWorkItemUseCase(repository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), cancellation.Token));
    }

    private sealed class FakeWorkItemRepository(bool deleted) : IWorkItemRepository
    {
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Guid DeletedId { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletedId = id;
            ReceivedToken = cancellationToken;
            return Task.FromResult(deleted);
        }

        public Task AddAsync(WorkItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
