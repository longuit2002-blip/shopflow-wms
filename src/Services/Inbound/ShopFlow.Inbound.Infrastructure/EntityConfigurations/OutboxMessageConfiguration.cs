using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the Inbound module's per-tenant <c>outbox_messages</c>
/// table. Mirrors the Inventory configuration shape — the
/// <see cref="OutboxMessage"/> type itself lives in the SharedKernel, but
/// each module owns its own EF configuration (per AGENTS.md §11.80
/// DbContext-per-module). The multiplexed dispatcher consumes this table
/// per-tenant via <c>MultiplexedOutboxDispatcher&lt;InboundDbContext&gt;</c>.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id).HasName("pk_outbox_messages");

        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        builder
            .Property(o => o.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(256)
            .IsRequired();
        builder
            .Property(o => o.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(o => o.TraceId).HasColumnName("trace_id").HasMaxLength(64);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.ProcessedAt).HasColumnName("processed_at");
        builder.Property(o => o.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(o => o.LastError).HasColumnName("last_error").HasMaxLength(2048);

        builder
            .HasIndex(o => new { o.ProcessedAt, o.CreatedAt })
            .HasDatabaseName("ix_outbox_messages_pending");
    }
}
