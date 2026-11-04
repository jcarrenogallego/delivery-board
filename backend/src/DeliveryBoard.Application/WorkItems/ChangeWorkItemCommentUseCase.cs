using DeliveryBoard.Domain.WorkItems;

namespace DeliveryBoard.Application.WorkItems;

public sealed class ChangeWorkItemCommentUseCase(IWorkItemRepository repository) : IChangeWorkItemCommentUseCase
{
    public async Task<WorkItemResponse?> ExecuteAsync(
        Guid id, ChangeWorkItemCommentRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saved = await repository.ChangeCommentAsync(id, request.Comment == string.Empty ? null : request.Comment, cancellationToken);
        return saved?.ToResponse();
    }
}
