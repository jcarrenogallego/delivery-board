using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;

namespace DeliveryBoard.Assistant.Infrastructure;

public static class AssistantServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AssistantOptions>(configuration.GetSection("Assistant"));
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.Configure<OpenAIOptions>(configuration.GetSection("OpenAI"));
        services.PostConfigure<OpenAIOptions>(options =>
        {
            // Explicit hierarchical configuration wins, including an intentionally empty value.
            if (configuration["OpenAI:ApiKey"] is null) options.ApiKey = configuration["OPENAI_API_KEY"];
        });
        services.Configure<McpOptions>(configuration.GetSection("Mcp"));
        services.AddSingleton<IAssistantChatClientFactory, AssistantChatClientFactory>();
        services.AddSingleton<IMcpToolSessionFactory, McpToolSessionFactory>();
        services.AddSingleton<IChatService, ChatService>();
        return services;
    }
}
