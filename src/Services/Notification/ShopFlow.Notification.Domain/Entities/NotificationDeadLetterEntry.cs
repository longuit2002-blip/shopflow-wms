namespace ShopFlow.Notification.Domain.Entities;

/// <summary>
/// Row shape for <c>notification_dead_letter</c> — terminal failure
/// record. The dispatcher (U3) inserts here when either the mailer
/// returns a <c>mailer.permanent.*</c> error code OR
/// <see cref="AttemptCount"/> reaches the configured ceiling on a
/// transient-error retry curve, then deletes the matching
/// <c>notification_outbox</c> row.
/// </summary>
/// <remarks>
/// <para>Sprint-9.5 does not expose this via REST — operators inspect
/// rows by direct DB query during incident response. Sprint-10+ may
/// add a <c>/admin/notification-dlq</c> tab (origin R10).</para>
/// <para><see cref="PayloadJson"/> preserves the full rendered email
/// JSON-serialized so a manual replay tool can re-submit if the
/// failure was transient-disguised-as-permanent (e.g. a misclassified
/// 5xx from an upstream gateway). Per ADR-0003 no <c>tenant_id</c>
/// column.</para>
/// </remarks>
public sealed class NotificationDeadLetterEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Upstream Auth event id (carries through for tracing).</summary>
    public Guid SourceEventId { get; set; }

    /// <summary>Lowercase-normalised recipient email.</summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>Notification kind as a string (matches the CHECK constraint).</summary>
    public string NotificationKind { get; set; } = string.Empty;

    /// <summary>Full rendered email JSON for replay tooling.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Number of attempts before the dispatcher dead-lettered the row.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Stable error code from the final attempt.</summary>
    public string LastErrorCode { get; set; } = string.Empty;

    /// <summary>Free-form error message captured at the final attempt.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>When the row was moved to the dead-letter table.</summary>
    public DateTime DeadLetteredAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
