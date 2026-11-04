using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

internal static class WorkItemMappings
{
    public static WorkItemResponse ToResponse(this WorkItem workItem) =>
        new(
            workItem.Id,
            workItem.Title,
            workItem.Owner,
            workItem.Priority.ToString(),
            workItem.Status.ToString(),
            workItem.CreatedAt,
            workItem.PrerequisiteIds.Length == 0 ? null : [.. workItem.PrerequisiteIds],
            Comment: workItem.Comment);
}
