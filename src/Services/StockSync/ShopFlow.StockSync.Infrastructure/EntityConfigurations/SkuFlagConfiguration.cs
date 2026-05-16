using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>stock_sync_sku_flag</c> (Sprint-5 plan U1/U7). The
/// primary key is the SKU string itself, mirroring Sprint-1-redux
/// <c>StockItem</c>: the inherited <see cref="Domain.Aggregates.SkuFlag.Id"/>
/// Guid from <c>BaseEntity</c> is ignored.
/// </summary>
internal sealed class SkuFlagConfiguration : IEntityTypeConfiguration<SkuFlag>
{
    public void Configure(EntityTypeBuilder<SkuFlag> builder)
    {
        builder.ToTable("stock_sync_sku_flag");

        builder.Ignore(s => s.Id);
        builder.Ignore(s => s.DomainEvents);

        builder.HasKey(s => s.Sku).HasName("pk_stock_sync_sku_flag");

        builder
            .Property(s => s.Sku)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(s => s.IsFlashSale)
            .HasColumnName("is_flash_sale")
            .IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}
