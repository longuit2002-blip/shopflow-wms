namespace ShopFlow.Notification.Domain.Entities;

/// <summary>
/// Row shape for <c>notification_log</c> — terminal success record. The
/// dispatcher (U3) inserts here after <c>IMailerProvider.SendAsync</c>
/// returns <c>Result.Success</c>, then deletes the matching
/// <c>notification_outbox</c> row in the same transaction.
/// </summary>
/// <remarks>
/// <para>KTD3 idempotency anchor — the table carries
/// <c>UNIQUE(source_event_id, recipient_email)</c>. A duplicate MT
/// redelivery races a second outbox row, but the second INSERT into
/// <c>notification_log</c> fails on UNIQUE and the dispatcher drops
/// the duplicate outbox row at debug log level. No double-send.</para>
/// <para>Per ADR-0003 no <c>tenant_id</c> column.</para>
/// </remarks>
public sealed class NotificationLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Upstream Auth event id; part of the UNIQUE dedup anchor.</summary>
    public Guid SourceEventId { get; set; }

    /// <summary>Lowercase-normalised recipient email; part of the UNIQUE dedup anchor.</summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>Notification kind as a string (matches the CHECK constraint).</summary>
    public string NotificationKind { get; set; } = string.Empty;

    /// <summary>
    /// SMTP-server-issued message id from <c>IMailerProvider.SendAsync</c>'s
    /// <c>Result&lt;MessageId&gt;</c> success payload. Allows tracing a
    /// delivery to the provider's logs.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>SMTP response code captured at delivery (e.g. "250 OK"); optional diagnostic.</summary>
    public string? ProviderResponseCode { get; set; }

    public DateTime SentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
