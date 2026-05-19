using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>outbound_saga_transitions</c> per Sprint-7 R14. Module
/// prefix per Sprint-2.5 cross-module-table-name convention. Indexed on
/// <c>(order_id, occurred_at DESC)</c> for R15's list query.
/// </summary>
internal sealed class OrderTransitionConfiguration : IEntityTypeConfiguration<OrderTransition>
{
    public void Configure(EntityTypeBuilder<OrderTransition> builder)
    {
        builder.ToTable("outbound_saga_transitions");

        builder.Ignore(o => o.DomainEvents);

        builder.HasKey(o => o.Id).HasName("pk_outbound_saga_transitions");
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.OrderId).HasColumnName("order_id").IsRequired();

        builder
            .Property(o => o.FromState)
            .HasColumnName("from_state")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(o => o.ToState)
            .HasColumnName("to_state")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder
            .Property(o => o.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(128)
            .IsRequired();

        builder
            .Property(o => o.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasIndex(o => new { o.OrderId, o.OccurredAt })
            .HasDatabaseName("ix_outbound_saga_transitions_order_occurred");
    }
}
