namespace DeliveryBoard.Infrastructure.Assistant;

public sealed class AssistantClientOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5092";
    public int TimeoutSeconds { get; set; } = 210;
}
