namespace DeliveryBoard.Domain.Exceptions;

public sealed class InvalidWorkItemPriorityException(string message) : Exception(message);
