using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>reconciliation_tickets</c> per Sprint-2-redux plan R9.
/// Append-only in Sprint-2-redux; resolution workflow lands later.
/// </summary>
internal sealed class ReconciliationTicketConfiguration
    : IEntityTypeConfiguration<ReconciliationTicket>
{
    public void Configure(EntityTypeBuilder<ReconciliationTicket> builder)
    {
        builder.ToTable("reconciliation_tickets");

        builder.Ignore(t => t.DomainEvents);
        // VarianceQty is a derived property — not stored.
        builder.Ignore(t => t.VarianceQty);

        builder.HasKey(t => t.Id).HasName("pk_reconciliation_tickets");
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.PurchaseOrderId).HasColumnName("purchase_order_id").IsRequired();
        builder
            .Property(t => t.PurchaseOrderLineId)
            .HasColumnName("purchase_order_line_id")
            .IsRequired();
        builder.Property(t => t.ReceivingId).HasColumnName("receiving_id").IsRequired();
        builder.Property(t => t.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        builder.Property(t => t.ExpectedQty).HasColumnName("expected_qty").IsRequired();
        builder.Property(t => t.ActualQty).HasColumnName("actual_qty").IsRequired();

        builder
            .Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(t => t.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        // Hot path: open tickets ordered by occurrence — Phase-2 resolution
        // workflow will read this.
        builder
            .HasIndex(t => new { t.Status, t.OccurredAt })
            .HasDatabaseName("ix_reconciliation_tickets_status_occurred_at");
    }
}
