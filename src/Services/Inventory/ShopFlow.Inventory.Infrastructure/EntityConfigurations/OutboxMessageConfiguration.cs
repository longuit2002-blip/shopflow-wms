using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Maps the kernel's <see cref="OutboxMessage"/> row shape to
/// <c>outbox_messages</c>. The kernel does not own a DbContext, so each
/// module that uses the outbox declares the table here. Schema mirrors
/// Tech Design §11.1 verbatim. Monthly partitioning is a follow-up at
/// scale tier 2; Phase 0 keeps the table plain.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(e => e.EventType).HasColumnName("event_type").IsRequired();

        builder.Property(e => e.Payload).HasColumnName("payload").IsRequired();

        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasMaxLength(64);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");

        builder.Property(e => e.RetryCount).HasColumnName("retry_count").IsRequired();

        builder.Property(e => e.LastError).HasColumnName("last_error");

        builder
            .HasIndex(e => new { e.ProcessedAt, e.CreatedAt })
            .HasDatabaseName("ix_outbox_unprocessed");
    }
}
