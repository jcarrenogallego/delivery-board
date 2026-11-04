using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class GetDashboardUseCase(IWorkItemRepository repository) : IGetDashboardUseCase
{
    public async Task<DashboardResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var items = await repository.GetAllAsync(cancellationToken);

        var completedIds = items.Where(item => item.Status == WorkItemStatus.Completed).Select(item => item.Id).ToHashSet();
        bool IsBlocked(WorkItem item) => item.Status == WorkItemStatus.Pending && item.PrerequisiteIds.Any(id => !completedIds.Contains(id));

        return new DashboardResponse(
            items.Count,
            items.Count(item => item.Status == WorkItemStatus.Pending),
            items.Count(item => item.Status == WorkItemStatus.InProgress),
            items.Count(item => item.Status == WorkItemStatus.Completed),
            [.. items.Select(item => item.ToResponse() with { IsBlocked = IsBlocked(item) })],
            items.Count(IsBlocked));
    }
}
