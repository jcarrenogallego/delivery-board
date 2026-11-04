using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.WorkItems;
using DeliveryBoard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeliveryBoard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationAndInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("deliveryboard")
            ?? throw new InvalidOperationException("No se encontró la conexión 'deliveryboard'.");

        services.AddScoped<IChangeWorkItemCommentUseCase, ChangeWorkItemCommentUseCase>();
        services.AddScoped<ICreateWorkItemUseCase, CreateWorkItemUseCase>();
        services.AddScoped<IChangeWorkItemDependenciesUseCase, ChangeWorkItemDependenciesUseCase>();
        services.AddScoped<IStartWorkItemUseCase, StartWorkItemUseCase>();
        services.AddScoped<ICompleteWorkItemUseCase, CompleteWorkItemUseCase>();
        services.AddScoped<IReassignWorkItemOwnerUseCase, ReassignWorkItemOwnerUseCase>();
        services.AddScoped<IChangeWorkItemPriorityUseCase, ChangeWorkItemPriorityUseCase>();
        services.AddScoped<IDeleteWorkItemUseCase, DeleteWorkItemUseCase>();
        services.AddScoped<IGetDashboardUseCase, GetDashboardUseCase>();
        services.AddScoped<IWorkItemRepository, WorkItemRepository>();
        services.AddDbContext<DeliveryBoardDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
