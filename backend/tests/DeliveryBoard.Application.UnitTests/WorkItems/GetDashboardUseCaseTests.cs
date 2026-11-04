using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class GetDashboardUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsTotalsAndItems()
    {
        var items = new[]
        {
            WorkItem.Create("Revisar incidencia", "Luis", WorkItemPriority.Medium, DateTimeOffset.UtcNow),
            WorkItem.Create("Actualizar API", "Marta", WorkItemPriority.High, DateTimeOffset.UtcNow)
        };
        var useCase = new GetDashboardUseCase(new FakeWorkItemRepository(items));

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Pending);
        Assert.Equal(2, result.Items.Count);
    }

    private sealed class FakeWorkItemRepository(IReadOnlyCollection<WorkItem> items) : IWorkItemRepository
    {
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(WorkItem workItem, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(items);

        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
