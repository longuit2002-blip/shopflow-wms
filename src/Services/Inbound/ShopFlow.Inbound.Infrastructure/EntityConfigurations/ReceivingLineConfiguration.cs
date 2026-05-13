using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>receiving_lines</c> per Sprint-2-redux plan R5-R6. The
/// load-bearing index is <c>UNIQUE(receiving_id, purchase_order_line_id)</c>
/// — the idempotency anchor for confirmed lines per plan R6.
/// </summary>
internal sealed class ReceivingLineConfiguration : IEntityTypeConfiguration<ReceivingLine>
{
    public void Configure(EntityTypeBuilder<ReceivingLine> builder)
    {
        builder.ToTable("receiving_lines");

        builder.Ignore(l => l.DomainEvents);

        builder.HasKey(l => l.Id).HasName("pk_receiving_lines");
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.ReceivingId).HasColumnName("receiving_id").IsRequired();
        builder
            .Property(l => l.PurchaseOrderLineId)
            .HasColumnName("purchase_order_line_id")
            .IsRequired();
        builder.Property(l => l.ActualQty).HasColumnName("actual_qty").IsRequired();
        builder.Property(l => l.SuggestedBinId).HasColumnName("suggested_bin_id").IsRequired();
        builder.Property(l => l.ActualBinId).HasColumnName("actual_bin_id").IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasIndex(l => new { l.ReceivingId, l.PurchaseOrderLineId })
            .IsUnique()
            .HasDatabaseName("ux_receiving_lines_receiving_line");
    }
}
