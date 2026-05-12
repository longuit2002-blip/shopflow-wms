using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>reservations_ledger</c> per Tech Design v3.0 §4.2.
/// The load-bearing index is <c>ux_reservations_order_id</c> —
/// <c>UNIQUE(order_id)</c>, NOT <c>UNIQUE(tenant_id, order_id)</c>, per
/// ADR-0003. Idempotent retries on the same <c>order_id</c> are caught at
/// the index level; the application layer surfaces them as the existing
/// reservation row.
/// </summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations_ledger");

        builder.Ignore(r => r.DomainEvents);

        builder.HasKey(r => r.Id).HasName("pk_reservations_ledger");

        builder.Property(r => r.Id).HasColumnName("id");

        builder
            .Property(r => r.Sku)
            .HasColumnName("sku")
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(v => v.Value, v => Sku.Create(v))
            .IsRequired();

        builder
            .Property(r => r.OrderId)
            .HasColumnName("order_id")
            .HasMaxLength(128)
            .IsRequired();

        builder
            .HasIndex(r => r.OrderId)
            .IsUnique()
            .HasDatabaseName("ux_reservations_order_id");

        builder
            .Property(r => r.Quantity)
            .HasColumnName("quantity")
            .HasConversion(v => v.Value, v => Quantity.From(v))
            .IsRequired();

        builder
            .Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(r => r.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(r => r.ReleasedAt).HasColumnName("released_at");
        builder.Property(r => r.ExpiredAt).HasColumnName("expired_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        // Hot path: the expiry worker scans Pending rows ordered by expires_at.
        builder
            .HasIndex(r => new { r.Status, r.ExpiresAt })
            .HasDatabaseName("ix_reservations_status_expires_at");

        // Foreign key to stock_items by SKU value.
        builder
            .HasOne<StockItem>()
            .WithMany()
            .HasForeignKey(r => r.Sku)
            .HasPrincipalKey(s => s.Sku)
            .HasConstraintName("fk_reservations_stock_items_sku")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
