using DeliveryBoard.Api.Endpoints;
using DeliveryBoard.Api.Errors;
using DeliveryBoard.Infrastructure;
using DeliveryBoard.Infrastructure.Assistant;
using DeliveryBoard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddApplicationAndInfrastructure(builder.Configuration);
builder.Services.AddAssistantQueryServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Delivery Board API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("Health");
app.MapWorkItemEndpoints();
app.MapAssistantEndpoints();

await ApplyMigrationsAsync(app);
await app.RunAsync();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>();
    await dbContext.Database.MigrateAsync();

    var readerPassword = app.Configuration["McpReader:Password"];
    if (!string.IsNullOrWhiteSpace(readerPassword))
    {
        await McpReaderProvisioner.ProvisionAsync(
            dbContext.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The database connection is not configured."),
            readerPassword,
            app.Lifetime.ApplicationStopping);
    }
}
