using System.Net;
using DeliveryBoard.Mcp.Application;
using DeliveryBoard.Mcp.Infrastructure;
using DeliveryBoard.Mcp.Tools;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
// Explicit local binding also applies when a caller supplies an unsafe ASPNETCORE_URLS.
var urls = builder.Configuration["urls"] ?? "http://localhost:5091";
var configuredUrls = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)
    .Concat(builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren()
        .Select(endpoint => endpoint["Url"]).OfType<string>());
foreach (var url in configuredUrls)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsLocalHost(uri.Host))
        throw new InvalidOperationException("The MCP server must bind to loopback addresses.");
}
builder.WebHost.UseUrls(urls);
builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
var connectionString = builder.Configuration.GetConnectionString("deliveryboard")
    ?? throw new InvalidOperationException("ConnectionStrings:deliveryboard is required.");
var connection = new NpgsqlConnectionStringBuilder(connectionString);
if (builder.Configuration["McpReader:Password"] is { Length: > 0 } password) connection.Password = password;
connection.IncludeErrorDetail = false;
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connection.ConnectionString));
builder.Services.AddSingleton<IReadOnlyDatabase, PostgreSqlReadOnlyDatabase>();
builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true).WithTools<DatabaseTools>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if ((remote is not null && !IPAddress.IsLoopback(remote)) || !IsLocalHost(context.Request.Host.Host))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden; return;
    }
    if (context.Request.Headers.TryGetValue("Origin", out var origins)
        && (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || !IsLocalHost(origin.Host) || origin.Scheme != context.Request.Scheme
            || origin.Authority != context.Request.Host.Value))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden; return;
    }
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMcp("/mcp");
await app.RunAsync();

static bool IsLocalHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
    || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

public partial class Program;
