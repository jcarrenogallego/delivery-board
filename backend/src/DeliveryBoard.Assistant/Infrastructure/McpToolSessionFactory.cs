using System.Net;
using System.Text.Json;
using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DeliveryBoard.Assistant.Infrastructure;

public sealed class McpToolSessionFactory(IOptions<McpOptions> options) : IMcpToolSessionFactory
{
    private static readonly HashSet<string> AllowedTools = new(StringComparer.Ordinal)
        { "get_database_schema", "execute_read_query" };

    public async Task<IMcpToolSession> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Value.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != "http" && endpoint.Scheme != "https") || !IsLoopback(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new AssistantException(503, "configuration_missing", "A local MCP endpoint must be configured.");

        McpClient? client = null;
        // Redirects must not escape the loopback endpoint validated above.
        var httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            { Timeout = Timeout.InfiniteTimeSpan };
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(30)
        }, httpClient, ownsHttpClient: true);
        try
        {
            client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new Session(client, tools.Where(tool => AllowedTools.Contains(tool.Name)).ToArray());
        }
        catch (Exception exception)
        {
            if (client is not null) await client.DisposeAsync();
            else await transport.DisposeAsync();
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            throw new AssistantException(503, "mcp_unavailable", "The Delivery Board MCP is unavailable.", exception);
        }
    }

    private static bool IsLoopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private sealed class Session(McpClient client, McpClientTool[] tools) : IMcpToolSession
    {
        public IReadOnlyList<AITool> Tools { get; } = tools;

        public async Task<object?> InvokeAsync(string name, IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            if (!AllowedTools.Contains(name) || !tools.Any(tool => tool.Name == name))
                throw new AssistantException(502, "tool_not_allowed", "The model requested an unavailable tool.");
            try
            {
                var result = await client.CallToolAsync(name,
                    arguments is null ? null : new Dictionary<string, object?>(arguments), cancellationToken: cancellationToken);
                if (result.IsError is true)
                    throw new AssistantException(502, "mcp_tool_failure", "A Delivery Board query could not be completed.",
                        mcpDiagnostic: new(name, ReadSafeErrorCode(result), "tool"));
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not AssistantException)
            { throw new AssistantException(502, "mcp_tool_failure", "A Delivery Board query could not be completed.",
                mcpDiagnostic: new(name, "invocation_error", "transport")); }
        }

        private static string ReadSafeErrorCode(CallToolResult result)
        {
            foreach (var block in result.Content.OfType<TextContentBlock>())
            {
                try
                {
                    using var json = JsonDocument.Parse(block.Text);
                    if (json.RootElement.ValueKind != JsonValueKind.Object
                        || !json.RootElement.TryGetProperty("error", out var error)
                        || error.ValueKind != JsonValueKind.Object
                        || !error.TryGetProperty("code", out var code)
                        || code.ValueKind != JsonValueKind.String) continue;
                    var value = code.GetString();
                    if (value is "query_rejected" or "query_cancelled" or "query_timeout" or "read_only_violation"
                        or "database_error" or "internal_error") return value;
                }
                catch (JsonException) { /* Untrusted error content must never enter diagnostics. */ }
            }
            return "tool_error";
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
