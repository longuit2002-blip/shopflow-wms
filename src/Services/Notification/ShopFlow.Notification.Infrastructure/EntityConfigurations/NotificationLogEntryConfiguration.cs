using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9.5 U1 fluent map for the per-tenant <c>notification_log</c>
/// table — terminal-success record + KTD3 idempotency anchor via
/// <c>UNIQUE(source_event_id, recipient_email)</c>. The U3 dispatcher
/// relies on the UNIQUE to fail-fast on a duplicate MT redelivery
/// (Npgsql SQLState 23505), which lets the dispatcher silently drop
/// the redundant outbox row at debug log level instead of double-
/// sending.
/// </summary>
internal sealed class NotificationLogEntryConfiguration
    : IEntityTypeConfiguration<NotificationLogEntry>
{
    public void Configure(EntityTypeBuilder<NotificationLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_log");

        builder.HasKey(l => l.Id).HasName("pk_notification_log");

        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.SourceEventId).HasColumnName("source_event_id").IsRequired();
        builder
            .Property(l => l.RecipientEmail)
            .HasColumnName("recipient_email")
            .HasMaxLength(254)
            .IsRequired();
        builder
            .Property(l => l.NotificationKind)
            .HasColumnName("notification_kind")
            .HasMaxLength(32)
            .IsRequired();
        builder
            .Property(l => l.MessageId)
            .HasColumnName("message_id")
            .HasMaxLength(998)
            .IsRequired();
        builder
            .Property(l => l.ProviderResponseCode)
            .HasColumnName("provider_response_code")
            .HasMaxLength(64);
        builder.Property(l => l.SentAt).HasColumnName("sent_at").IsRequired();
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // KTD3 idempotency anchor.
        builder
            .HasIndex(l => new { l.SourceEventId, l.RecipientEmail })
            .HasDatabaseName("ux_notification_log_source_event_recipient")
            .IsUnique();
    }
}
