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

const string connection = "Host=127.0.0.1;Port=55436;Database=comments_test;Username=commenttest;Password=comment-local-only";
var sql = new SqlCapture();
var ct = CancellationToken.None;
ServiceProvider Services(string database)
{
    var cs = new NpgsqlConnectionStringBuilder(connection) { Database = database }.ConnectionString;
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:deliveryboard"] = cs }).Build();
    var services = new ServiceCollection().AddApplicationAndInfrastructure(config);
    services.AddDbContext<DeliveryBoardDbContext>(options => options.AddInterceptors(sql));
    return services.BuildServiceProvider();
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
await using var admin = new NpgsqlConnection(connection);
await admin.OpenAsync();
foreach (var legacy in new[] { false, true })
{
    // Names are generated here; no existing database is migrated or dropped.
    var database = "comments_verify_" + Guid.NewGuid().ToString("N");
    await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
    try
    {
        using var provider = Services(database);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>();
        var legacyId = Guid.NewGuid();
        if (legacy)
        {
            await db.GetService<IMigrator>().MigrateAsync("20261014210804_AddWorkItemDependencies");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO work_items (\"Id\", \"Title\", \"Owner\", \"Priority\", \"Status\", \"CreatedAt\") VALUES ({legacyId}, {"Legacy"}, {"Ana"}, {"Low"}, {"Completed"}, {DateTimeOffset.UnixEpoch})");
        }
        await db.Database.MigrateAsync();
        if (legacy)
        {
            var old = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == legacyId);
            Check(old.Comment is null && old.Title == "Legacy" && old.Owner == "Ana" && old.Priority == WorkItemPriority.Low && old.Status == WorkItemStatus.Completed && old.CreatedAt == DateTimeOffset.UnixEpoch && old.PrerequisiteIds.Length == 0, "Migration changed legacy data");
            await scope.ServiceProvider.GetRequiredService<IChangeWorkItemCommentUseCase>().ExecuteAsync(old.Id, new("Legacy comment"), ct);
        }
        var repo = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var other = WorkItem.Create("Control", "Bea", WorkItemPriority.Low, DateTimeOffset.UnixEpoch, "Control");
        var item = WorkItem.Create("Target", "Ana", WorkItemPriority.High, DateTimeOffset.UnixEpoch);
        item.SetPrerequisites([other.Id]);
        await repo.AddAsync(other, ct);
        await repo.AddAsync(item, ct);
        var before = (item.Id, item.Title, item.Owner, item.Priority, item.Status, item.CreatedAt);
        foreach (var status in Enum.GetValues<WorkItemStatus>())
        {
            if (status == WorkItemStatus.InProgress) item.Start();
            if (status == WorkItemStatus.Completed) item.Complete();
            await repo.SaveChangesAsync(ct);
            foreach (var text in new string?[] { "  Unicode 🧪\n<script>literal</script>  ", "Replacement", "", null, "  " })
            {
                var saved = await scope.ServiceProvider.GetRequiredService<IChangeWorkItemCommentUseCase>().ExecuteAsync(item.Id, new(text), ct);
                using var read = provider.CreateScope();
                var readDb = read.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>();
                var fresh = await readDb.WorkItems.AsNoTracking().SingleAsync(x => x.Id == item.Id);
                Check(saved!.Comment == (text == "" ? null : text) && fresh.Comment == saved.Comment, "Comment did not persist");
                Check((fresh.Id, fresh.Title, fresh.Owner, fresh.Priority, fresh.CreatedAt) == (before.Id, before.Title, before.Owner, before.Priority, before.CreatedAt) && fresh.Status == status && fresh.PrerequisiteIds.SequenceEqual(new[] { other.Id }), "Comment changed another field");
                Check((await readDb.WorkItems.AsNoTracking().SingleAsync(x => x.Id == other.Id)).Comment == "Control", "Changed another task");
            }
        }
        // Load both snapshots BEFORE writes. The stale writer must only save its changed properties.
        var race = WorkItem.Create("Race", "Ana", WorkItemPriority.Medium, DateTimeOffset.UnixEpoch, "Before");
        await repo.AddAsync(race, ct);
        using var second = provider.CreateScope();
        var b = second.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var oldB = (await b.GetByIdAsync(race.Id, ct))!;
        await repo.ChangeCommentAsync(race.Id, "Saved after snapshot", ct);
        oldB.ReassignOwner("Bea", "Ana");
        oldB.SetPrerequisites([other.Id]);
        oldB.Start();
        await b.SaveChangesAsync(ct);
        using (var afterStaleSave = provider.CreateScope())
        {
            var persisted = await afterStaleSave.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>().WorkItems.AsNoTracking().SingleAsync(x => x.Id == race.Id);
            Check(persisted.Comment == "Saved after snapshot", "Stale Owner/Status/dependency save overwrote comment");
        }
        await b.ChangePriorityAsync(race.Id, WorkItemPriority.Low, ct);
        using (var afterPriority = provider.CreateScope())
        {
            var persisted = await afterPriority.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>().WorkItems.AsNoTracking().SingleAsync(x => x.Id == race.Id);
            Check(persisted.Comment == "Saved after snapshot", "Priority update overwrote comment");
        }
        var final = (await repo.ChangeCommentAsync(race.Id, "Final", ct))!;
        Check(final.Owner == "Bea" && final.Status == WorkItemStatus.InProgress && final.Priority == WorkItemPriority.Low && final.PrerequisiteIds.SequenceEqual(new[] { other.Id }), "Comment overwrote other concurrent edits");
        Check(!ReferenceEquals(final, race), "Response reused tracked stale object");
        using (var read = provider.CreateScope())
        {
            var fresh = await read.ServiceProvider.GetRequiredService<DeliveryBoardDbContext>().WorkItems.AsNoTracking().SingleAsync(x => x.Id == race.Id);
            Check(fresh.Comment == "Final", "Stale write overwrote comment");
        }
        race.Complete();
        try { await repo.SaveChangesAsync(ct); throw new Exception("Owner/Status concurrency tokens lost"); }
        catch (WorkItemConflictException) { }
        Check(await b.ChangeCommentAsync(Guid.NewGuid(), "Missing", ct) is null, "Missing task created");
        var updates = sql.Commands.Where(x => x.StartsWith("UPDATE work_items AS", StringComparison.Ordinal) && x.Contains("SET \"Comment\"", StringComparison.Ordinal)).ToArray();
        Check(updates.Length > 0, "No captured SQL");
        foreach (var command in updates)
        {
            var set = command[(command.IndexOf("SET", StringComparison.Ordinal) + 3)..command.IndexOf("WHERE", StringComparison.Ordinal)];
            Check(set.Trim().StartsWith("\"Comment\" =", StringComparison.Ordinal) && !set.Contains(','), "UPDATE changes other columns");
        }
        Console.WriteLine(updates[0]);
        Console.WriteLine($"PASS {(legacy ? "legacy" : "fresh")}: migration; 3 states; add/replace/remove; literal Unicode; new-context reads; dependencies/control preserved; controlled stale snapshots; Owner/Status conflict; Comment-only SQL; missing Id");
    }
    finally
    {
        NpgsqlConnection.ClearAllPools();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
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
