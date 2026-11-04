using System.Data.Common;
using DeliveryBoard.Application.WorkItems;
using DeliveryBoard.Domain.Exceptions;
using DeliveryBoard.Domain.WorkItems;
using DeliveryBoard.Infrastructure;
using DeliveryBoard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

const string connection = "Host=127.0.0.1;Port=55432;Database=deliveryboard;Username=prioritytest;Password=priority-test-local-only";
var ct = CancellationToken.None;
var sql = new SqlCapture();
ServiceProvider Services(string cs)
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:deliveryboard"] = cs }).Build();
    var collection = new ServiceCollection().AddApplicationAndInfrastructure(config);
    collection.AddDbContext<DeliveryBoardDbContext>(options => options.AddInterceptors(sql));
    return collection.BuildServiceProvider();
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
using var services = Services(connection);
using (var scope = services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>().Database.MigrateAsync();

// A distinct disposable database represents the legacy schema. No other database is touched.
var legacyName = "priority_legacy_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(connection);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {legacyName}", admin)) await create.ExecuteNonQueryAsync();
try
{
    using var legacy = Services(new NpgsqlConnectionStringBuilder(connection) { Database = legacyName }.ConnectionString);
    using (var scope = legacy.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261001230037_ProtectWorkItemReassignment");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE work_items ALTER COLUMN \"Priority\" DROP NOT NULL;");
        foreach (var value in new string?[] { null, "Low", "Medium", "High" })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO work_items (\"Id\", \"Title\", \"Owner\", \"Priority\", \"Status\", \"CreatedAt\") VALUES ({Guid.NewGuid()}, {value ?? "NULL control"}, {"Ana"}, {value}, {"Pending"}, {DateTimeOffset.UtcNow})");
        }
        var nullCount = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM work_items WHERE \"Priority\" IS NULL").SingleAsync();
        Check(nullCount == 1, "Fixture must contain one genuine SQL NULL before migration");
        await db.Database.MigrateAsync();
    }
    using (var read = legacy.CreateScope())
    {
        var items = await read.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetAllAsync(ct);
        foreach (var item in items)
            Check(item.Priority.ToString() == (item.Title == "NULL control" ? "Medium" : item.Title), "Migration overwrote valid priority");
        var db = read.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>();
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO work_items (\"Id\", \"Title\", \"Owner\", \"Status\", \"CreatedAt\") VALUES ({id}, {"SQL omitted priority"}, {"Ana"}, {"Pending"}, {DateTimeOffset.UtcNow})");
        Check((await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == id)).Priority == WorkItemPriority.Medium, "SQL default missing");
    }
    Console.WriteLine("PASS legacy genuine NULL backfill, Low/Medium/High controls, fresh read, SQL default Medium");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE {legacyName} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
}

var ids = new List<Guid>();
async Task<WorkItem> NewItem(WorkItemPriority priority = WorkItemPriority.Medium)
{
    using var scope = services.CreateScope();
    var item = WorkItem.Create("VERIFICACION prioridad", "Ana", priority, DateTimeOffset.UtcNow);
    await scope.ServiceProvider.GetRequiredService<IWorkItemRepository>().AddAsync(item, ct);
    ids.Add(item.Id);
    using var read = services.CreateScope();
    Check((await read.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetByIdAsync(item.Id, ct))!.Priority == priority, "EF explicit Low replaced by SQL default");
    return item;
}
try
{
    foreach (var priority in Enum.GetValues<WorkItemPriority>()) await NewItem(priority);
    Console.WriteLine("PASS EF inserts preserve explicit Low=0, Medium, High");
    var item = await NewItem();
    using var first = services.CreateScope();
    using var second = services.CreateScope();
    var a = first.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    var b = second.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    var oldA = (await a.GetByIdAsync(item.Id, ct))!;
    var oldB = (await b.GetByIdAsync(item.Id, ct))!;
    // Compare two persisted reads; the factory timestamp has finer precision than PostgreSQL.
    var before = (oldA.Id, oldA.Title, oldA.Owner, oldA.Status, oldA.CreatedAt);
    await a.ChangePriorityAsync(item.Id, WorkItemPriority.High, ct);
    var final = (await b.ChangePriorityAsync(item.Id, WorkItemPriority.Medium, ct))!;
    Check(final.Priority == WorkItemPriority.Medium, "Old snapshot equality skipped final write");
    Check(before == (final.Id, final.Title, final.Owner, final.Status, final.CreatedAt), "Priority changed unrelated fields");
    Check(!ReferenceEquals(final, oldB), "Priority response reused stale tracking");
    Console.WriteLine("PASS two preloaded clients: last save wins even when requested value equals old snapshot; fresh response");

    oldB.ReassignOwner("Bea", "Ana");
    oldB.Start();
    await b.SaveChangesAsync(ct);
    final = (await a.ChangePriorityAsync(item.Id, WorkItemPriority.Low, ct))!;
    Check(final.Owner == "Bea" && final.Status == WorkItemStatus.InProgress && final.Priority == WorkItemPriority.Low, "Priority overwrote concurrent Owner/Status");
    oldA.Complete();
    try { await a.SaveChangesAsync(ct); throw new Exception("Owner/Status tokens no longer conflict"); }
    catch (WorkItemConflictException) { }
    using var fresh = services.CreateScope();
    var repo = fresh.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    var live = (await repo.GetByIdAsync(item.Id, ct))!;
    live.Complete();
    await repo.SaveChangesAsync(ct);
    final = (await repo.ChangePriorityAsync(item.Id, WorkItemPriority.High, ct))!;
    Check(final.Status == WorkItemStatus.Completed && final.Owner == "Bea", "Completed priority change failed");
    Check(await repo.ChangePriorityAsync(Guid.NewGuid(), WorkItemPriority.Low, ct) is null, "Missing Id not null");
    Console.WriteLine("PASS concurrent owner/start survive priority; Owner/Status stale writes still conflict; Completed editing; missing Id");

    var updates = sql.Commands.Where(x => x.StartsWith("UPDATE work_items AS", StringComparison.Ordinal) && x.Contains("SET \"Priority\"")).ToArray();
    Check(updates.Length >= 4, "Priority SQL not captured");
    foreach (var command in updates)
    {
        var set = command[(command.IndexOf("SET", StringComparison.Ordinal) + 3)..command.IndexOf("WHERE", StringComparison.Ordinal)];
        Check(!set.Contains("\"Owner\"") && !set.Contains("\"Status\"") && !set.Contains("\"Title\""), "SQL modified another field");
        Console.WriteLine(command);
    }
    Console.WriteLine("PASS captured real PostgreSQL UPDATE changes only Priority by Id");
}
finally
{
    using var cleanup = services.CreateScope();
    var repo = cleanup.ServiceProvider.GetRequiredService<IWorkItemRepository>();
    foreach (var id in ids) await repo.DeleteAsync(id, ct);
}

sealed class SqlCapture : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
