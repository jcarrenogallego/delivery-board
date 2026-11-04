using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

internal static class WorkItemPriorityParser
{
    public static WorkItemPriority Parse(string? value, bool defaultWhenMissing = false)
    {
        if (value is null && defaultWhenMissing) return WorkItemPriority.Medium;
        return value?.Trim().ToUpperInvariant() switch
        {
            "LOW" => WorkItemPriority.Low,
            "MEDIUM" => WorkItemPriority.Medium,
            "HIGH" => WorkItemPriority.High,
            _ => throw new InvalidWorkItemPriorityException("La prioridad debe ser Low, Medium o High.")
        };
    }
}
