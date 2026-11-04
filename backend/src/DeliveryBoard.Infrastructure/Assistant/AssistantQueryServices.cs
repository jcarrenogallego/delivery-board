using DeliveryBoard.Application.Assistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeliveryBoard.Infrastructure.Assistant;

public static class AssistantQueryServices
{
    public static IServiceCollection AddAssistantQueryServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AssistantClientOptions>(configuration.GetSection("AssistantClient"));
        services.AddScoped<IQueryAssistantUseCase, QueryAssistantUseCase>();
        services.AddHttpClient<IAssistantQueryClient, AssistantQueryHttpClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        return services;
    }
}
