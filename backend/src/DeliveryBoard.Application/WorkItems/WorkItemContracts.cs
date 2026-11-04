namespace DeliveryBoard.Application.WorkItems;

public sealed record CreateWorkItemRequest(string Title, string Owner, string? Priority = null, string? Comment = null);

public sealed record ChangeWorkItemDependenciesRequest(Guid[]? PrerequisiteIds);

/// <summary>Replaces the comment; null or empty removes it.</summary>
public sealed record ChangeWorkItemCommentRequest(string? Comment);

public sealed record ChangeWorkItemPriorityRequest(string? Priority);

public sealed record ReassignWorkItemOwnerRequest(string? Owner, string? ExpectedOwner);

public sealed record StartWorkItemResponse(WorkItemResponse WorkItem, bool Changed);

public sealed record WorkItemResponse(
    Guid Id,
    string Title,
    string Owner,
    string Priority,
    string Status,
    DateTimeOffset CreatedAt,
    Guid[]? PrerequisiteIds = null,
    bool IsBlocked = false,
    string? Comment = null);

public sealed record DashboardResponse(
    int Total,
    int Pending,
    int InProgress,
    int Completed,
    IReadOnlyCollection<WorkItemResponse> Items,
    int Blocked = 0);
