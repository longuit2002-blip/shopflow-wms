using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>purchase_orders</c> per Sprint-2-redux plan R3.
/// </summary>
internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.Ignore(p => p.DomainEvents);

        builder.HasKey(p => p.Id).HasName("pk_purchase_orders");
        builder.Property(p => p.Id).HasColumnName("id");

        builder
            .Property(p => p.SupplierRef)
            .HasColumnName("supplier_ref")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(p => p.ExpectedDeliveryAt).HasColumnName("expected_delivery_at").IsRequired();

        builder
            .Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(24)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(p => p.OpenedAt).HasColumnName("opened_at");
        builder.Property(p => p.ClosedAt).HasColumnName("closed_at");
        builder.Property(p => p.CancelledAt).HasColumnName("cancelled_at");
        builder
            .Property(p => p.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(512);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasMany(p => p.Lines)
            .WithOne()
            .HasForeignKey(l => l.PurchaseOrderId)
            .HasConstraintName("fk_po_lines_purchase_orders")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Status).HasDatabaseName("ix_purchase_orders_status");
    }
}
