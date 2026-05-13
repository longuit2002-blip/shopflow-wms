using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the Outbound module's per-tenant
/// <c>outbound_outbox_messages</c> table. Per-module prefix per the
/// Sprint-2.5 convention (see
/// <c>docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md</c>)
/// — Outbound + Inbound + Inventory all share one physical tenant DB
/// under ADR-0003, so a single <c>outbox_messages</c> would collide.
/// The <see cref="MultiplexedOutboxDispatcher{TContext}"/> reads via
/// EF's entity-config-driven <c>DbSet&lt;OutboxMessage&gt;</c> — no
/// dispatcher change required.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbound_outbox_messages");

        builder.HasKey(o => o.Id).HasName("pk_outbound_outbox_messages");

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
            .HasDatabaseName("ix_outbound_outbox_messages_pending");
    }
}
