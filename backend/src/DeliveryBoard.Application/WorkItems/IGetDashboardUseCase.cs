namespace DeliveryBoard.Application.WorkItems;

public interface IGetDashboardUseCase
{
    Task<DashboardResponse> ExecuteAsync(CancellationToken cancellationToken);
}
