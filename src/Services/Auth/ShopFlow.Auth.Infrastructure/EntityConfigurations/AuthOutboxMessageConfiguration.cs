using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9 U3 fluent map for the Auth module's per-tenant
/// <c>auth_outbox_messages</c> table. Per-module prefix per
/// Sprint-2.5 (see
/// <c>docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md</c>)
/// — every business module that ships an outbox uses a prefix so a
/// single shared tenant DB doesn't collide. Sprint-9 is the first Auth
/// outbox because Sprint-8 had no cross-module events; Sprint-9 emits
/// four (PasswordResetRequestedV1 / RefreshReuseDetectedV1 /
/// AccountLockedV1 / MfaEnrolledV1).
/// </summary>
internal sealed class AuthOutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("auth_outbox_messages");

        builder.HasKey(o => o.Id).HasName("pk_auth_outbox_messages");

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
            .HasDatabaseName("ix_auth_outbox_messages_pending");
    }
}
