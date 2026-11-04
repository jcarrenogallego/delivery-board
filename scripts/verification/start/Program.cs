using System.Diagnostics;
using System.Text.Json;
using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;
using DeliveryBoard.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 2 || !int.TryParse(args[1], out var port))
    throw new ArgumentException("Indica nombre del contenedor PostgreSQL de este proyecto y puerto local publicado.");
var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, UseShellExecute = false };
foreach (var argument in new[] { "inspect", args[0], "--format", "{{json .Config.Env}}" }) start.ArgumentList.Add(argument);
var process = Process.Start(start)!;
var env = JsonSerializer.Deserialize<string[]>(await process.StandardOutput.ReadToEndAsync())!;
await process.WaitForExitAsync();
string Value(string key) => env.Single(x => x.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
var connection = $"Host=localhost;Port={port};Database=deliveryboard;Username={Value("POSTGRES_USER")};Password={Value("POSTGRES_PASSWORD")}";
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{ ["ConnectionStrings:deliveryboard"] = connection }).Build();
using var services = new ServiceCollection().AddApplicationAndInfrastructure(config).BuildServiceProvider();
var ids = new List<Guid>();
var ct = CancellationToken.None;
async Task Conflict(Func<Task> write)
{
    try { await write(); throw new Exception("Expected concurrency conflict"); }
    catch (WorkItemConflictException) { }
}
async Task<WorkItem> NewItem()
{
    using var scope = services.CreateScope();
    var item = WorkItem.Create("VERIFICACION CX inicio 20261009", "Ana", WorkItemPriority.Medium, DateTimeOffset.UtcNow);
    await scope.ServiceProvider.GetRequiredService<IWorkItemRepository>().AddAsync(item, ct);
    ids.Add(item.Id);
    return item;
}
async Task Race(string name, Action<WorkItem> firstChange, Action<WorkItem> secondChange, string expectedStatus, string expectedOwner)
{
    var item = await NewItem();
    using var first = services.CreateScope();
    using var second = services.CreateScope();
    var a = first.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    var b = second.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    var left = (await a.GetByIdAsync(item.Id, ct))!;
    var right = (await b.GetByIdAsync(item.Id, ct))!;
    if (left.Status != WorkItemStatus.Pending || right.Status != WorkItemStatus.Pending) throw new Exception("Snapshot");
    firstChange(left); secondChange(right);
    await a.SaveChangesAsync(ct);
    await Conflict(() => b.SaveChangesAsync(ct));
    using var read = services.CreateScope();
    var saved = (await read.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetByIdAsync(item.Id, ct))!;
    if (saved.Status.ToString() != expectedStatus || saved.Owner != expectedOwner) throw new Exception("Overwrite");
    if (name == "CX-1")
    {
        var repetition = await read.ServiceProvider.GetRequiredService<IStartWorkItemUseCase>().ExecuteAsync(item.Id, ct);
        if (repetition is null || repetition.Changed) throw new Exception("Repetition");
    }
    Console.WriteLine(name + " PASS (two real contexts loaded before either save; second conflicts)");
}
try
{
    await Race("CX-1", x => x.Start(), x => x.Start(), "InProgress", "Ana");
    await Race("CX-2 start first", x => x.Start(), x => x.Complete(), "InProgress", "Ana");
    await Race("CX-2 complete first", x => x.Complete(), x => x.Start(), "Completed", "Ana");
    await Race("CX-3 start first", x => x.Start(), x => x.ReassignOwner("Bea", "Ana"), "InProgress", "Ana");
    await Race("CX-3 owner first", x => x.ReassignOwner("Bea", "Ana"), x => x.Start(), "Pending", "Bea");
    var item = await NewItem();
    using var first = services.CreateScope();
    var repo = first.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    var snapshot = (await repo.GetByIdAsync(item.Id, ct))!;
    using var delete = services.CreateScope();
    await delete.ServiceProvider.GetRequiredService<IWorkItemRepository>().DeleteAsync(item.Id, ct);
    snapshot.Start();
    await Conflict(() => repo.SaveChangesAsync(ct));
    using var missing = services.CreateScope();
    if (await missing.ServiceProvider.GetRequiredService<IStartWorkItemUseCase>().ExecuteAsync(item.Id, ct) is not null) throw new Exception("Missing");
    Console.WriteLine("CX-4 PASS (loaded Pending, deleted in second context, stale save conflicts; fresh read null)");
}
finally
{
    using var cleanup = services.CreateScope();
    var repo = cleanup.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    foreach (var id in ids) await repo.DeleteAsync(id, ct);
}
