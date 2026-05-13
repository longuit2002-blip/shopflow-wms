using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>purchase_order_lines</c> per Sprint-2-redux plan R3.
/// Part of the PO aggregate; configured by the kernel-wide pattern as a
/// child entity with its own table + FK back to <see cref="PurchaseOrder"/>.
/// </summary>
internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines");

        builder.Ignore(l => l.DomainEvents);

        builder.HasKey(l => l.Id).HasName("pk_purchase_order_lines");
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.PurchaseOrderId).HasColumnName("purchase_order_id").IsRequired();

        builder.Property(l => l.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        builder.Property(l => l.ExpectedQty).HasColumnName("expected_qty").IsRequired();
        builder.Property(l => l.ReceivedQty).HasColumnName("received_qty").IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasIndex(l => new { l.PurchaseOrderId, l.Sku })
            .HasDatabaseName("ix_po_lines_po_id_sku");
    }
}
