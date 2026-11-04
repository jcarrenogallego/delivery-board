using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.UnitTests.WorkItems;

public sealed class CreateWorkItemUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidData_CreatesPendingWorkItem()
    {
        var repository = new FakeWorkItemRepository();
        var useCase = new CreateWorkItemUseCase(repository);

        var result = await useCase.ExecuteAsync(
            new CreateWorkItemRequest("Preparar demo", "Ana", "High"),
            CancellationToken.None);

        Assert.Equal("Preparar demo", result.Title);
        Assert.Equal("High", result.Priority);
        Assert.Equal("Pending", result.Status);
        Assert.Single(repository.Items);
    }

    [Theory]
    [InlineData("Urgent")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("Low, High")]
    [InlineData("")]
    public async Task ExecuteAsync_WithUnknownPriority_ThrowsPriorityException(string priority)
    {
        var repository = new FakeWorkItemRepository();
        var useCase = new CreateWorkItemUseCase(repository);

        var action = () => useCase.ExecuteAsync(
            new CreateWorkItemRequest("Preparar demo", "Ana", priority),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidWorkItemPriorityException>(action);
        Assert.Empty(repository.Items);
    }

    [Theory]
    [InlineData(null, "Medium")]
    [InlineData("Low", "Low")]
    [InlineData("Medium", "Medium")]
    [InlineData("High", "High")]
    public async Task ExecuteAsync_PreservesCatalogOrDefaultsMissing(string? priority, string expected)
    {
        var result = await new CreateWorkItemUseCase(new FakeWorkItemRepository()).ExecuteAsync(
            new CreateWorkItemRequest("Preparar demo", "Ana", priority), CancellationToken.None);
        Assert.Equal(expected, result.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_DoesNotWrite()
    {
        var repository = new FakeWorkItemRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateWorkItemUseCase(repository).ExecuteAsync(
            new CreateWorkItemRequest("Demo", "Ana"), new CancellationToken(true)));
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutTitle_ThrowsBusinessRuleException()
    {
        var repository = new FakeWorkItemRepository();
        var useCase = new CreateWorkItemUseCase(repository);

        var action = () => useCase.ExecuteAsync(
            new CreateWorkItemRequest(" ", "Ana", "High"),
            CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(action);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOwner_ThrowsBusinessRuleException()
    {
        var repository = new FakeWorkItemRepository();
        var useCase = new CreateWorkItemUseCase(repository);

        var action = () => useCase.ExecuteAsync(
            new CreateWorkItemRequest("Preparar demo", " ", "High"),
            CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(action);
        Assert.Empty(repository.Items);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", "  ")]
    [InlineData("  Contexto 🧪\n<script>literal</script>  ", "  Contexto 🧪\n<script>literal</script>  ")]
    public async Task ExecuteAsync_WithOptionalComment_PersistsAndReadsLiteralText(string? comment, string? expected)
    {
        var repository = new FakeWorkItemRepository();
        var result = await new CreateWorkItemUseCase(repository).ExecuteAsync(
            new CreateWorkItemRequest("Demo", "Ana", Comment: comment), CancellationToken.None);
        Assert.Equal(expected, result.Comment);
        Assert.Equal(expected, Assert.Single(repository.Items).Comment);
        var dashboard = await new GetDashboardUseCase(repository).ExecuteAsync(CancellationToken.None);
        Assert.Equal(expected, Assert.Single(dashboard.Items).Comment);
        Assert.Equal(1, dashboard.Pending);
    }

    private sealed class FakeWorkItemRepository : IWorkItemRepository
    {
        public Task<WorkItem?> ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkItem?> ChangePriorityAsync(Guid id, WorkItemPriority priority, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public List<WorkItem> Items { get; } = [];

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(WorkItem workItem, CancellationToken cancellationToken)
        {
            Items.Add(workItem);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<WorkItem>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<WorkItem>>(Items);

        public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
