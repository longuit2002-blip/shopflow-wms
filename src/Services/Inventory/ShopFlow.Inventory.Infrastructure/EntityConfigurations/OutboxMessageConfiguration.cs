using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the per-tenant <c>outbox_messages</c> table. Each
/// module's tenant DB gets one — the multiplexed dispatcher in the
/// SharedKernel (<see cref="MultiplexedOutboxDispatcher{TContext}"/> if
/// wired) fans out across tenants by reading
/// <see cref="OutboxMessage.TenantId"/> against the active catalog.
/// </summary>
/// <remarks>
/// The <c>tenant_id</c> column is technically redundant — the DB
/// identifies the tenant — but is retained per the
/// <see cref="OutboxMessage"/> doc comment so envelope construction does
/// not need a per-message catalog round-trip and so cross-tenant routing
/// assertions in tests have a clear diagnostic signal.
/// </remarks>
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

        // Dispatcher hot path: pending rows ordered by created_at.
        builder
            .HasIndex(o => new { o.ProcessedAt, o.CreatedAt })
            .HasDatabaseName("ix_outbox_messages_pending");
    }
}
