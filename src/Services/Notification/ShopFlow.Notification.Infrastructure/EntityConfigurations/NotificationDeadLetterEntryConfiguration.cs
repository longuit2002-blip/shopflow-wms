using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9.5 U1 fluent map for the per-tenant
/// <c>notification_dead_letter</c> table — terminal-failure store. No
/// UNIQUE here (duplicate DLQ rows are acceptable; the matching outbox
/// row is always deleted alongside). Indexed by
/// <c>dead_lettered_at</c> for the operator-side "recent failures"
/// inspection query.
/// </summary>
internal sealed class NotificationDeadLetterEntryConfiguration
    : IEntityTypeConfiguration<NotificationDeadLetterEntry>
{
    public void Configure(EntityTypeBuilder<NotificationDeadLetterEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_dead_letter");

        builder.HasKey(d => d.Id).HasName("pk_notification_dead_letter");

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.SourceEventId).HasColumnName("source_event_id").IsRequired();
        builder
            .Property(d => d.RecipientEmail)
            .HasColumnName("recipient_email")
            .HasMaxLength(254)
            .IsRequired();
        builder
            .Property(d => d.NotificationKind)
            .HasColumnName("notification_kind")
            .HasMaxLength(32)
            .IsRequired();
        builder
            .Property(d => d.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(d => d.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder
            .Property(d => d.LastErrorCode)
            .HasColumnName("last_error_code")
            .HasMaxLength(128)
            .IsRequired();
        builder
            .Property(d => d.LastErrorMessage)
            .HasColumnName("last_error_message")
            .HasMaxLength(2048);
        builder
            .Property(d => d.DeadLetteredAt)
            .HasColumnName("dead_lettered_at")
            .IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder
            .HasIndex(d => d.DeadLetteredAt)
            .HasDatabaseName("ix_notification_dead_letter_dead_lettered_at");
    }
}
