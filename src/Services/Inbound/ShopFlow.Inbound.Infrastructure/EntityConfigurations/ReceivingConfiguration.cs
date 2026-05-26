using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>receivings</c> per Sprint-2-redux plan R4-R5.
/// </summary>
internal sealed class ReceivingConfiguration : IEntityTypeConfiguration<Receiving>
{
    public void Configure(EntityTypeBuilder<Receiving> builder)
    {
        builder.ToTable("receivings");

        builder.Ignore(r => r.DomainEvents);

        builder.HasKey(r => r.Id).HasName("pk_receivings");
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.PurchaseOrderId).HasColumnName("purchase_order_id").IsRequired();
        builder.Property(r => r.OperatorId).HasColumnName("operator_id");
        builder.Property(r => r.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(r => r.PurchaseOrderId)
            .HasConstraintName("fk_receivings_purchase_orders")
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.ReceivingId)
            .HasConstraintName("fk_receiving_lines_receivings")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.PurchaseOrderId).HasDatabaseName("ix_receivings_purchase_order_id");
    }
}
