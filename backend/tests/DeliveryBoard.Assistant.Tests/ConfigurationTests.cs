using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using DeliveryBoard.Assistant.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;

namespace DeliveryBoard.Assistant.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData(null, "fallback", "fallback")]
    [InlineData("configured", "fallback", "configured")]
    [InlineData("", "fallback", "")]
    public void HierarchicalKeyHasPriorityOverFallback(string? configured, string fallback, string expected)
    {
        var values = new Dictionary<string, string?> { ["OPENAI_API_KEY"] = fallback };
        if (configured is not null) values["OpenAI:ApiKey"] = configured;
        using var services = new ServiceCollection().AddAssistantServices(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
        Assert.Equal(expected, services.GetRequiredService<IOptions<OpenAIOptions>>().Value.ApiKey);
    }

    [Fact]
    public void MissingOpenAiConfigurationDoesNotDisableOllama()
    {
        var factory = new AssistantChatClientFactory(Options.Create(new OllamaOptions { Model = "local-model" }),
            Options.Create(new OpenAIOptions()));
        Assert.Equal(new ChatProviderMetadata("ollama", "local-model"), factory.GetProvider("ollama"));
        Assert.Equal(503, Assert.Throws<AssistantException>(() => factory.GetProvider("openai")).StatusCode);
        using var client = factory.Create("ollama");
        Assert.NotNull(client.GetService<OllamaSharp.IOllamaApiClient>());
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    public void ModelsAreRequiredPerProvider(string provider)
    {
        var factory = new AssistantChatClientFactory(Options.Create(new OllamaOptions()),
            Options.Create(new OpenAIOptions { ApiKey = "test-key" }));
        Assert.Equal("configuration_missing", Assert.Throws<AssistantException>(() => factory.GetProvider(provider)).Code);
    }

    [Fact]
    public void OpenAiUsesConfiguredModelWithoutMakingRequests()
    {
        var factory = new AssistantChatClientFactory(Options.Create(new OllamaOptions()),
            Options.Create(new OpenAIOptions { Model = "configured-openai-model", ApiKey = "test-key" }));
        Assert.Equal("configured-openai-model", factory.GetProvider("openai").Model);
        using var client = factory.Create("openai");
        Assert.NotNull(client);
    }
}
