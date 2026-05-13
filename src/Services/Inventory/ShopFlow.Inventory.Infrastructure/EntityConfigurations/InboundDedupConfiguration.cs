using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>inbound_dedup</c> per Sprint-2-redux plan R11. The
/// composite PK is the idempotency anchor; a duplicate INSERT trips
/// <c>23505</c> which the consumer catches.
/// </summary>
internal sealed class InboundDedupConfiguration : IEntityTypeConfiguration<InboundDedup>
{
    public void Configure(EntityTypeBuilder<InboundDedup> builder)
    {
        builder.ToTable("inbound_dedup");
        builder.HasKey(d => new { d.ReceivingId, d.LineId }).HasName("pk_inbound_dedup");
        builder.Property(d => d.ReceivingId).HasColumnName("receiving_id").IsRequired();
        builder.Property(d => d.LineId).HasColumnName("line_id").IsRequired();
        builder.Property(d => d.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        builder.Property(d => d.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(d => d.ProcessedAt).HasColumnName("processed_at").IsRequired();
    }
}
