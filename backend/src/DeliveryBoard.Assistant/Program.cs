using System.Net;
using DeliveryBoard.Assistant.Endpoints;
using DeliveryBoard.Assistant.Errors;
using DeliveryBoard.Assistant.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var urls = builder.Configuration["urls"] ?? "http://localhost:5092";
var configuredUrls = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)
    .Concat(builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren()
        .Select(endpoint => endpoint["Url"]).OfType<string>());
foreach (var url in configuredUrls)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsLoopback(uri.Host))
        throw new InvalidOperationException("The assistant must bind to loopback addresses.");
}
builder.WebHost.UseUrls(urls);
builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddAssistantServices(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if ((remote is not null && !IPAddress.IsLoopback(remote)) || !IsLoopback(context.Request.Host.Host))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    if (context.Request.Headers.TryGetValue("Origin", out var origins)
        && (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || origin.Scheme != context.Request.Scheme || origin.Authority != context.Request.Host.Value))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next(context);
});
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/swagger/index.html")).ExcludeFromDescription();
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Delivery Board Assistant v1");
        options.RoutePrefix = "swagger";
    });
}
app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" })).WithName("AssistantHealth");
app.MapAssistantEndpoints();
await app.RunAsync();

static bool IsLoopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
    || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

public partial class Program;
