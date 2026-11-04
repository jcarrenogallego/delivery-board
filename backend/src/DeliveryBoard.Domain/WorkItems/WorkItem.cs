using DeliveryBoard.Domain.Exceptions;

namespace DeliveryBoard.Domain.WorkItems;

public sealed class WorkItem
{
    private WorkItem()
    {
    }

    private WorkItem(Guid id, string title, string owner, WorkItemPriority priority, DateTimeOffset createdAt, string? comment)
    {
        ChangeComment(comment);
        Id = id;
        Title = title;
        Owner = owner;
        Priority = priority;
        CreatedAt = createdAt;
        Status = WorkItemStatus.Pending;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Comment { get; private set; }

    public void ChangeComment(string? comment) => Comment = comment == string.Empty ? null : comment;

    public string Owner { get; private set; } = string.Empty;

    public WorkItemPriority Priority { get; private set; } = WorkItemPriority.Medium;

    public Guid[] PrerequisiteIds { get; private set; } = [];

    public void SetPrerequisites(Guid[] ids)
    {
        if (Status != WorkItemStatus.Pending)
            throw new WorkItemConflictException("Solo las tareas pendientes pueden cambiar sus dependencias.");
        if (ids.Contains(Id) || ids.Distinct().Count() != ids.Length)
            throw new InvalidWorkItemDependencyException("No se permiten dependencias propias ni duplicadas.");
        PrerequisiteIds = [.. ids];
    }

    public WorkItemStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static WorkItem Create(string title, string owner, WorkItemPriority priority, DateTimeOffset createdAt, string? comment = null)
    {
        ValidatePriority(priority);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BusinessRuleException("El título es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new BusinessRuleException("El responsable es obligatorio.");
        }

        return new WorkItem(Guid.NewGuid(), title.Trim(), owner.Trim(), priority, createdAt, comment);
    }

    public void ChangePriority(WorkItemPriority priority)
    {
        ValidatePriority(priority);
        Priority = priority;
    }

    private static void ValidatePriority(WorkItemPriority priority)
    {
        if (!Enum.IsDefined(priority))
            throw new InvalidWorkItemPriorityException("La prioridad debe ser Low, Medium o High.");
    }

    public bool ReassignOwner(string? owner, string? expectedOwner)
    {
        var normalizedOwner = ValidateOwner(owner, "El responsable");
        var normalizedExpectedOwner = ValidateOwner(expectedOwner, "El responsable anterior");

        if (Status == WorkItemStatus.Completed)
        {
            throw new WorkItemConflictException("No se puede cambiar el responsable de una tarea completada.");
        }

        if (!string.Equals(Owner, normalizedExpectedOwner, StringComparison.Ordinal))
        {
            throw new WorkItemConflictException("El responsable ha cambiado. Actualiza el tablero antes de volver a intentarlo.");
        }

        if (string.Equals(Owner, normalizedOwner, StringComparison.Ordinal))
        {
            return false;
        }

        Owner = normalizedOwner;
        return true;
    }

    private static string ValidateOwner(string? owner, string field)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new BusinessRuleException($"{field} es obligatorio.");
        }

        var normalizedOwner = owner.Trim();
        if (normalizedOwner.Length > 80)
        {
            throw new BusinessRuleException($"{field} no puede superar los 80 caracteres.");
        }

        return normalizedOwner;
    }

    public bool Start()
    {
        if (Status == WorkItemStatus.Completed)
        {
            throw new WorkItemConflictException("No se puede iniciar una tarea completada.");
        }

        if (Status == WorkItemStatus.InProgress)
        {
            return false;
        }

        Status = WorkItemStatus.InProgress;
        return true;
    }

    public bool Complete()
    {
        if (Status == WorkItemStatus.Completed)
        {
            return false;
        }

        Status = WorkItemStatus.Completed;
        return true;
    }
}

public enum WorkItemPriority
{
    Low,
    Medium,
    High
}

public enum WorkItemStatus
{
    Pending,
    InProgress,
    Completed
}
