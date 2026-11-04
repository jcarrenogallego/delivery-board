using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using DeliveryBoard.Assistant.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DeliveryBoard.Assistant.Tests;

public sealed class McpAdapterTests
{
    [Fact]
    public async Task DiscoversOnlyAllowedToolsAndRejectsToolErrors()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true).WithTools<TestTools>();
        await using var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            var factory = new McpToolSessionFactory(Options.Create(new McpOptions { Endpoint = address + "/mcp" }));
            await using var session = await factory.ConnectAsync(CancellationToken.None);
            Assert.Equal(new[] { "execute_read_query", "get_database_schema" }, session.Tools.Select(tool => tool.Name).Order());
            var result = Assert.IsType<CallToolResult>(await session.InvokeAsync("get_database_schema", null, CancellationToken.None));
            Assert.Contains("fixture_schema", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            var denied = await Assert.ThrowsAsync<AssistantException>(() => session.InvokeAsync("delete_everything", null, CancellationToken.None));
            Assert.Equal("tool_not_allowed", denied.Code);
            var failure = await Assert.ThrowsAsync<AssistantException>(() => session.InvokeAsync("execute_read_query",
                new Dictionary<string, object?> { ["sql"] = "SELECT 1" }, CancellationToken.None));
            Assert.Equal("mcp_tool_failure", failure.Code);
            Assert.DoesNotContain("sensitive", failure.SafeMessage);
            Assert.Equal(new("execute_read_query", "tool_error", "tool"), failure.McpDiagnostic);
            Assert.Null(failure.InnerException);
            foreach (var code in new[] { "query_rejected", "query_cancelled", "query_timeout", "read_only_violation",
                "database_error", "internal_error", "unknown-sensitive-code", "malformed", "wrong-shape" })
            {
                var diagnosticFailure = await Assert.ThrowsAsync<AssistantException>(() => session.InvokeAsync("execute_read_query",
                    new Dictionary<string, object?> { ["sql"] = code }, CancellationToken.None));
                var expectedCode = code is "unknown-sensitive-code" or "malformed" or "wrong-shape" ? "tool_error" : code;
                Assert.Equal(new("execute_read_query", expectedCode, "tool"), diagnosticFailure.McpDiagnostic);
                Assert.Null(diagnosticFailure.InnerException);
                Assert.DoesNotContain("sensitive", diagnosticFailure.ToString());
            }
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("https://example.com/mcp")]
    [InlineData("file:///mcp")]
    [InlineData("http://user:secret@localhost/mcp")]
    [InlineData("")]
    public async Task RejectsNonLocalOrInvalidConfigurationBeforeConnecting(string endpoint)
    {
        var factory = new McpToolSessionFactory(Options.Create(new McpOptions { Endpoint = endpoint }));
        var failure = await Assert.ThrowsAsync<AssistantException>(() => factory.ConnectAsync(CancellationToken.None));
        Assert.Equal(503, failure.StatusCode);
    }

    [McpServerToolType]
    public sealed class TestTools
    {
        [McpServerTool(Name = "get_database_schema", ReadOnly = true)]
        public static string GetSchema() => "fixture_schema";

        [McpServerTool(Name = "execute_read_query", ReadOnly = true)]
        public static CallToolResult Query(string sql) => new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = sql switch
            {
                "SELECT 1" => "sensitive internal database detail",
                "malformed" => "{sensitive malformed json",
                "wrong-shape" => "{\"error\":\"sensitive\"}",
                _ => System.Text.Json.JsonSerializer.Serialize(new { error = new { code = sql,
                    message = "sensitive SQL credentials database contents" } })
            } }]
        };

        [McpServerTool(Name = "delete_everything")]
        public static string DeleteEverything() => throw new InvalidOperationException("Must never execute.");
    }
}
