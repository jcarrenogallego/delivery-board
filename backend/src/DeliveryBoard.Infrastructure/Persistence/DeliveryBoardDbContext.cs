using DeliveryBoard.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DeliveryBoard.Infrastructure.Persistence;

public sealed class DeliveryBoardDbContext(DbContextOptions<DeliveryBoardDbContext> options)
    : DbContext(options)
{
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveryBoardDbContext).Assembly);
}
