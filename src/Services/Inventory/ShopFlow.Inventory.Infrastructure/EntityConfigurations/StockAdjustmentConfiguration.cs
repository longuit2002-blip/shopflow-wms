using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>stock_adjustments</c> per Tech Design v3.0 §4.2. One
/// row per non-reservation delta applied to <see cref="StockItem.Available"/>;
/// the audit trail that lets dashboards reconcile inbound receipts vs
/// cycle-count corrections vs damage write-offs.
/// </summary>
internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.ToTable("stock_adjustments");

        builder.Ignore(s => s.DomainEvents);

        builder.HasKey(s => s.Id).HasName("pk_stock_adjustments");

        builder.Property(s => s.Id).HasColumnName("id");

        builder
            .Property(s => s.Sku)
            .HasColumnName("sku")
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(v => v.Value, v => Sku.Create(v))
            .IsRequired();

        builder.Property(s => s.Delta).HasColumnName("delta").IsRequired();

        builder
            .Property(s => s.Reason)
            .HasColumnName("reason")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(s => s.Note).HasColumnName("note").HasMaxLength(512);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasIndex(s => new { s.Sku, s.CreatedAt })
            .HasDatabaseName("ix_stock_adjustments_sku_created_at");

        builder
            .HasOne<StockItem>()
            .WithMany()
            .HasForeignKey(s => s.Sku)
            .HasPrincipalKey(item => item.Sku)
            .HasConstraintName("fk_stock_adjustments_stock_items_sku")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
