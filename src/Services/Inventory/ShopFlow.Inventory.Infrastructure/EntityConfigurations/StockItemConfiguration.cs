using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>stock_items</c> per Tech Design v3.0 §4.2. SKU is
/// the natural PK; the inherited <c>BaseEntity.Id</c> Guid is ignored.
/// <c>row_version</c> uses Postgres <c>xid</c> with
/// <c>(txid_current())::text::xid</c> default — matches the control-plane
/// tenants pattern.
/// </summary>
internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items");

        builder.Ignore(s => s.Id);
        builder.Ignore(s => s.DomainEvents);

        builder.HasKey(s => s.Sku).HasName("pk_stock_items");

        builder
            .Property(s => s.Sku)
            .HasColumnName("sku")
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(v => v.Value, v => Sku.Create(v))
            .IsRequired();

        builder
            .Property(s => s.Available)
            .HasColumnName("available")
            .HasConversion(v => v.Value, v => Quantity.From(v))
            .IsRequired();

        builder
            .Property(s => s.Reserved)
            .HasColumnName("reserved")
            .HasConversion(v => v.Value, v => Quantity.From(v))
            .IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder
            .Property(s => s.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .HasColumnType("xid")
            .HasDefaultValueSql("(txid_current())::text::xid");

        builder.Property(s => s.HomeZoneId).HasColumnName("home_zone_id");

        builder
            .HasOne<Zone>()
            .WithMany()
            .HasForeignKey(s => s.HomeZoneId)
            .HasConstraintName("fk_stock_items_zones")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
