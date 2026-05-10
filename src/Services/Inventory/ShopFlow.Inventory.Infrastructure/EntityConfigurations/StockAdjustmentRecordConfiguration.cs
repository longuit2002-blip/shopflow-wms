using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Maps <see cref="StockAdjustmentRecord"/> to <c>stock_adjustments</c>
/// (Tech Design §21.1). Plain (non-partitioned) at Phase 0; monthly
/// partitioning lands at scale tier 2.
/// </summary>
internal sealed class StockAdjustmentRecordConfiguration
    : IEntityTypeConfiguration<StockAdjustmentRecord>
{
    private readonly IRequestContext _requestContext;

    public StockAdjustmentRecordConfiguration(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public void Configure(EntityTypeBuilder<StockAdjustmentRecord> builder)
    {
        builder.ToTable("stock_adjustments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(e => e.StockItemId).HasColumnName("stock_item_id").IsRequired();

        builder.Property(e => e.QuantityDelta).HasColumnName("quantity_delta").IsRequired();

        builder
            .Property(e => e.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(1024);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder
            .HasIndex(e => new
            {
                e.TenantId,
                e.StockItemId,
                e.CreatedAt,
            })
            .HasDatabaseName("ix_stock_adjustments_tenant_item_created");

        builder.HasQueryFilter(e => e.TenantId == _requestContext.TenantId);
    }
}
