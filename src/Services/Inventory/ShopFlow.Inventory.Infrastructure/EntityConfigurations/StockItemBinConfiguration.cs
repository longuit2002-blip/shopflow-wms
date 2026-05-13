using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>stock_item_bins</c> per Sprint-2-redux plan R13.
/// Composite PK (sku, bin_id). FKs are enforced at the database level by
/// the U4 migration (<c>fk_stock_item_bins_stock_items</c> +
/// <c>fk_stock_item_bins_bins</c>); not declared as EF navigations
/// because <c>StockItem.Sku</c> is a value-object (Sku) while this
/// table's <c>Sku</c> is the underlying string — EF's FK validator
/// rejects the type mismatch. The migration's FK is the authoritative
/// referential-integrity contract.
/// </summary>
internal sealed class StockItemBinConfiguration : IEntityTypeConfiguration<StockItemBin>
{
    public void Configure(EntityTypeBuilder<StockItemBin> builder)
    {
        builder.ToTable("stock_item_bins");
        builder.HasKey(s => new { s.Sku, s.BinId }).HasName("pk_stock_item_bins");
        builder.Property(s => s.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        builder.Property(s => s.BinId).HasColumnName("bin_id").IsRequired();
        builder.Property(s => s.Quantity).HasColumnName("quantity").IsRequired();
    }
}
