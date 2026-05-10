using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Maps <see cref="StockItem"/> to <c>stock_items</c> (Tech Design §7.2).
/// Composite primary key on <c>(tenant_id, sku)</c>; optimistic concurrency
/// via <c>row_version</c> rowversion (xmin under Postgres). Check
/// constraints for non-negative counters live in the migration's raw SQL
/// because EF Core's <c>HasCheckConstraint</c> is the more idiomatic place
/// — see <c>20260427000001_InitialInventorySchema.cs</c>.
/// </summary>
internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    private readonly IRequestContext _requestContext;

    public StockItemConfiguration(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items");

        builder.HasKey(e => new { e.TenantId, e.Sku });

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(e => e.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();

        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();

        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(128);

        builder.Property(e => e.TotalQuantity).HasColumnName("total_qty").IsRequired();

        builder.Property(e => e.AllocatedQuantity).HasColumnName("allocated_qty").IsRequired();

        builder.Property(e => e.SafetyThreshold).HasColumnName("safety_threshold").IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder
            .Property(e => e.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .IsConcurrencyToken();

        // Domain events are a transient buffer drained by the outbox
        // interceptor; never persisted on this entity.
        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => new { e.TenantId, e.Id }).HasDatabaseName("ix_stock_items_tenant_id");

        // Read-side global query filter per AGENTS.md §3 + Tech Design §4:
        // every read is implicitly scoped to the active tenant. Postgres
        // RLS is the second wall in front of the same data.
        builder.HasQueryFilter(e => e.TenantId == _requestContext.TenantId);
    }
}
