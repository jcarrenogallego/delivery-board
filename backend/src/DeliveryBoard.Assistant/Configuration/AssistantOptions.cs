namespace DeliveryBoard.Assistant.Configuration;

public sealed class AssistantOptions
{
    public int MaxToolRounds { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 180;
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string? Model { get; set; }
}

public sealed class OpenAIOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
}

public sealed class McpOptions
{
    public string? Endpoint { get; set; }
}
