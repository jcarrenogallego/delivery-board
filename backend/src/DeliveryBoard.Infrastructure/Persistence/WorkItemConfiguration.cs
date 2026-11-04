using DeliveryBoard.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryBoard.Infrastructure.Persistence;

internal sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("work_items");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Comment).HasColumnType("text");
        builder.Property(item => item.Title).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Owner).HasMaxLength(80).IsRequired().IsConcurrencyToken();
        builder.Property(item => item.Priority).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(WorkItemPriority.Medium).ValueGeneratedNever();
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
        builder.Property(item => item.PrerequisiteIds).HasColumnType("uuid[]").HasDefaultValueSql("ARRAY[]::uuid[]");
        builder.Property(item => item.CreatedAt).IsRequired();
    }
}
