using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Channel.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the Channel module's per-tenant
/// <c>channel_outbox_messages</c> table. Per-module prefix per Sprint-2.5
/// (see <c>docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md</c>).
/// The <see cref="MultiplexedOutboxDispatcher{TContext}"/> reads via EF's
/// entity-config-driven <c>DbSet&lt;OutboxMessage&gt;</c> — no dispatcher
/// change required for the rename. The K13 envelope-type → endpoint routing
/// (Sprint-4 U4) layers on top of this without touching the table shape.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("channel_outbox_messages");

        builder.HasKey(o => o.Id).HasName("pk_channel_outbox_messages");

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
            .HasDatabaseName("ix_channel_outbox_messages_pending");
    }
}
