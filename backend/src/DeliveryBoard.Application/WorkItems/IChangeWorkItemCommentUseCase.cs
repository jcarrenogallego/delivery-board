namespace DeliveryBoard.Application.WorkItems;

public interface IChangeWorkItemCommentUseCase
{
    Task<WorkItemResponse?> ExecuteAsync(Guid id, ChangeWorkItemCommentRequest request, CancellationToken cancellationToken);
}
