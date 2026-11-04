using System.Text.Json;
using DeliveryBoard.Assistant.Application;
using DeliveryBoard.Assistant.Configuration;
using DeliveryBoard.Assistant.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace DeliveryBoard.Assistant.Tests;

public sealed class ProviderWireSchemaTests
{
    [Fact]
    public async Task OllamaWireReceivesActualMcpSchemaAsToolDataBeforeFirstResponse()
    {
        JsonElement? captured = null;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost("/api/chat", async (HttpContext context) =>
        {
            using var json = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            captured = json.RootElement.Clone();
            context.Response.ContentType = "application/x-ndjson";
            await context.Response.WriteAsync("{\"model\":\"fixture-model\",\"created_at\":\"2026-10-23T00:00:00Z\","
                + "\"message\":{\"role\":\"assistant\",\"content\":\"Ana tiene 2 tareas.\"},\"done\":true,\"done_reason\":\"stop\"}\n",
                context.RequestAborted);
        });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var clients = new AssistantChatClientFactory(Options.Create(new OllamaOptions
                { BaseUrl = address, Model = "fixture-model" }), Options.Create(new OpenAIOptions()));
            var service = new ChatService(clients, new SchemaSessions(), Options.Create(new AssistantOptions()));
            List<string> text = [];
            await foreach (var delta in service.StreamAsync("ollama", [new("user", "cuántas tareas tiene Ana")])) text.Add(delta);

            Assert.Equal("Ana tiene 2 tareas.", string.Concat(text));
            Assert.NotNull(captured);
            var messages = captured.Value.GetProperty("messages").EnumerateArray().ToArray();
            var tool = Assert.Single(messages, message => message.GetProperty("role").GetString() == "tool");
            var content = tool.GetProperty("content").GetString()!;
            Assert.Contains("work_items", content);
            Assert.Contains("Owner", content);
            Assert.DoesNotContain("ModelContextProtocol.Protocol.CallToolResult", content);
            using var toolJson = JsonDocument.Parse(content);
            var structuredResult = Assert.Single(toolJson.RootElement.EnumerateObject(),
                property => property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.TryGetProperty("schemas", out _)).Value;
            var schema = structuredResult.GetProperty("schemas")[0];
            Assert.Equal("public", schema.GetProperty("name").GetString());
            var table = Assert.Single(schema.GetProperty("tables").EnumerateArray());
            Assert.Equal("work_items", table.GetProperty("name").GetString());
            Assert.Equal("Owner", Assert.Single(table.GetProperty("columns").EnumerateArray()).GetProperty("name").GetString());
            Assert.DoesNotContain("work_items", messages[0].GetProperty("content").GetString());
        }
        finally { await app.StopAsync(); }
    }

    private sealed class SchemaSessions : IMcpToolSessionFactory
    {
        public Task<IMcpToolSession> ConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IMcpToolSession>(new SchemaSession());
    }

    private sealed class SchemaSession : IMcpToolSession
    {
        public IReadOnlyList<AITool> Tools { get; } = [AIFunctionFactory.Create(() => "schema", "get_database_schema")];
        public Task<object?> InvokeAsync(string name, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
        {
            Assert.Equal("get_database_schema", name);
            return Task.FromResult<object?>(new CallToolResult
            {
                Content = [new TextContentBlock
                {
                    Text = "{\"schemas\":[{\"name\":\"public\",\"tables\":[{\"name\":\"work_items\",\"columns\":[{\"name\":\"Owner\",\"type\":\"text\"}]}]}]}"
                }]
            });
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
