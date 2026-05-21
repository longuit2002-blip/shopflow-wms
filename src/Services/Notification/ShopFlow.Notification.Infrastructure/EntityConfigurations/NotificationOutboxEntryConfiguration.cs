using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9.5 U1 fluent map for the per-tenant
/// <c>notification_outbox</c> table — the second-stage queue holding
/// rendered emails awaiting SMTP delivery. The U3 dispatcher polls
/// <c>(status, created_at)</c> for the next pending batch and reads by
/// <c>source_event_id</c> when reconciling redelivery.
/// </summary>
internal sealed class NotificationOutboxEntryConfiguration
    : IEntityTypeConfiguration<NotificationOutboxEntry>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_outbox");

        builder.HasKey(o => o.Id).HasName("pk_notification_outbox");

        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.SourceEventId).HasColumnName("source_event_id").IsRequired();
        builder
            .Property(o => o.NotificationKind)
            .HasColumnName("notification_kind")
            .HasMaxLength(32)
            .IsRequired();
        builder
            .Property(o => o.RecipientEmail)
            .HasColumnName("recipient_email")
            .HasMaxLength(254)
            .IsRequired();
        builder
            .Property(o => o.RecipientDisplayName)
            .HasColumnName("recipient_display_name")
            .HasMaxLength(256);
        builder
            .Property(o => o.RenderedSubject)
            .HasColumnName("rendered_subject")
            .HasMaxLength(998)
            .IsRequired();
        builder
            .Property(o => o.RenderedBodyText)
            .HasColumnName("rendered_body_text")
            .IsRequired();
        builder
            .Property(o => o.RenderedBodyHtml)
            .HasColumnName("rendered_body_html")
            .IsRequired();
        builder
            .Property(o => o.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasDefaultValue("pending")
            .IsRequired();
        builder
            .Property(o => o.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();
        builder.Property(o => o.LastAttemptAt).HasColumnName("last_attempt_at");
        builder
            .Property(o => o.LastErrorCode)
            .HasColumnName("last_error_code")
            .HasMaxLength(128);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder
            .HasIndex(o => new { o.Status, o.CreatedAt })
            .HasDatabaseName("ix_notification_outbox_pending");

        builder
            .HasIndex(o => o.SourceEventId)
            .HasDatabaseName("ix_notification_outbox_source_event");
    }
}
