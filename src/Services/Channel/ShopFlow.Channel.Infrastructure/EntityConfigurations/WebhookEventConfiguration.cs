using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Channel.Domain.Webhooks;

namespace ShopFlow.Channel.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>webhook_events</c> per Sprint-4 plan R3/U2. The
/// <c>ux_webhook_events_channel_provider_event</c> UNIQUE index on
/// <c>(channel_id, provider_event_id)</c> is the idempotency anchor —
/// duplicate webhooks land on the index and the receiver's repository
/// catches PostgresException 23505 (see <c>WebhookEventRepository</c> in U3
/// + Sprint-1-redux <c>ReservationRepository</c> pattern).
/// </summary>
internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        builder.Ignore(e => e.DomainEvents);

        builder.HasKey(e => e.Id).HasName("pk_webhook_events");
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ChannelId).HasColumnName("channel_id").IsRequired();

        // ProviderEventId is a value object — store as raw string with the
        // VO's Create factory enforcing length + trim invariants at write
        // time. EF reads the row back via the parameterless ctor + private
        // setter pattern (see Sku/ProviderEventId value-object convention).
        builder
            .Property(e => e.ProviderEventId)
            .HasColumnName("provider_event_id")
            .HasConversion(vo => vo.Value, str => ProviderEventId.Create(str).Value!)
            .HasMaxLength(ProviderEventId.MaxLength)
            .IsRequired();

        builder
            .Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.SignatureVerified).HasColumnName("signature_verified").IsRequired();

        builder
            .Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.FailureReason).HasColumnName("failure_reason").HasMaxLength(512);
        builder.Property(e => e.CreatedAt).HasColumnName("received_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasIndex(e => new { e.ChannelId, e.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("ux_webhook_events_channel_provider_event");
    }
}
