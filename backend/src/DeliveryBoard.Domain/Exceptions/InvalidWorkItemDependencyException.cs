namespace DeliveryBoard.Domain.Exceptions;

public sealed class InvalidWorkItemDependencyException(string message) : Exception(message);
