namespace DeliveryBoard.Domain.Exceptions;

public sealed class WorkItemConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
