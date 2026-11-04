using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var readerPassword = builder.AddParameter("mcp-reader-password", new GenerateParameterDefault
{
    MinLength = 32,
    Special = false
}, secret: true, persist: true);

var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume(name: builder.Configuration["Postgres:VolumeName"]);

var database = postgres.AddDatabase("deliveryboard");

var api = builder
    .AddProject<Projects.DeliveryBoard_Api>("api")
    .WithReference(database)
    .WithEnvironment("McpReader__Password", readerPassword)
    .WaitFor(database)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var mcpPort = builder.Configuration.GetValue("Mcp:Port", 5091);
var mcpUrl = $"http://127.0.0.1:{mcpPort}";
var mcp = builder
    .AddProject<Projects.DeliveryBoard_Mcp>("mcp", launchProfileName: null)
    .WithHttpEndpoint(port: mcpPort, targetPort: mcpPort, name: "http", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "127.0.0.1")
    .WithEnvironment("ASPNETCORE_URLS", mcpUrl)
    .WithEnvironment("ConnectionStrings__deliveryboard", ReferenceExpression.Create(
        $"Host={postgres.Resource.Host};Port={postgres.Resource.Port};Database=deliveryboard;Username=mcp_reader"))
    .WithEnvironment("McpReader__Password", readerPassword)
    .WaitFor(database)
    .WaitFor(api)
    .WithHttpHealthCheck("/health");

var assistantPort = builder.Configuration.GetValue("Assistant:Port", 5092);
var assistantUrl = $"http://127.0.0.1:{assistantPort}";
var assistant = builder
    .AddProject<Projects.DeliveryBoard_Assistant>("assistant", launchProfileName: null)
    .WithHttpEndpoint(port: assistantPort, targetPort: assistantPort, name: "http", isProxied: false)
    .WithEndpoint("http", endpoint => endpoint.TargetHost = "127.0.0.1")
    .WithEnvironment("ASPNETCORE_URLS", assistantUrl)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Mcp__Endpoint", $"{mcpUrl}/mcp")
    .WaitFor(mcp)
    .WithHttpHealthCheck("/health");

// Configure HTTP access without waiting: MCP already waits for this API to provision its reader.
api.WithEnvironment("AssistantClient__BaseUrl", assistantUrl);
if (builder.Configuration["AssistantClient:TimeoutSeconds"] is { Length: > 0 } assistantClientTimeout)
    api.WithEnvironment("AssistantClient__TimeoutSeconds", assistantClientTimeout);

foreach (var key in new[] { "Ollama:BaseUrl", "Ollama:Model", "OpenAI:Model", "Assistant:MaxToolRounds", "Assistant:RequestTimeoutSeconds" })
{
    if (builder.Configuration[key] is { Length: > 0 } value)
        assistant.WithEnvironment(key.Replace(":", "__"), value);
}

// Keep credentials as a secret parameter rather than a literal resource environment value.
var openAiKey = builder.AddParameter("openai-api-key", secret: true,
    value: builder.Configuration["OpenAI:ApiKey"] ?? builder.Configuration["OPENAI_API_KEY"] ?? string.Empty);
assistant.WithEnvironment("OpenAI__ApiKey", openAiKey);

builder
    .AddJavaScriptApp("frontend", "../../../frontend", "start")
    .WithHttpEndpoint(port: 3000, env: "PORT", name: "http")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
