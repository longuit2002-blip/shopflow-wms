namespace ShopFlow.Notification.Domain.Entities;

/// <summary>
/// Row shape for <c>notification_outbox</c> — the second-stage queue
/// holding fully-rendered emails awaiting SMTP delivery. Written by the
/// U3 MT consumers (one row per consumed Auth event, pre-rendered
/// payload); claimed by the U3 background dispatcher via
/// <c>FOR UPDATE SKIP LOCKED</c>; deleted on terminal success (or
/// when a row moves to <c>notification_dead_letter</c>).
/// </summary>
/// <remarks>
/// Per ADR-0003 no <c>tenant_id</c> column — the tenant DB is the
/// boundary. Tenant identity flows into the consumer via the
/// per-request <c>NotificationDbContext</c> binding from the catalog
/// router (Sprint-1-redux pattern).
/// </remarks>
public sealed class NotificationOutboxEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Upstream Auth event id; KTD3 idempotency anchor.</summary>
    public Guid SourceEventId { get; set; }

    /// <summary>Notification kind as a string (matches the CHECK constraint).</summary>
    public string NotificationKind { get; set; } = string.Empty;

    /// <summary>Lowercase-normalised recipient email.</summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>Optional display name; null if not known.</summary>
    public string? RecipientDisplayName { get; set; }

    /// <summary>Pre-rendered subject line (≤ 998 octets per RFC 5322).</summary>
    public string RenderedSubject { get; set; } = string.Empty;

    /// <summary>Pre-rendered plain-text body.</summary>
    public string RenderedBodyText { get; set; } = string.Empty;

    /// <summary>Pre-rendered HTML body (already HTML-escaped per KTD6).</summary>
    public string RenderedBodyHtml { get; set; } = string.Empty;

    /// <summary>
    /// Row lifecycle: <c>pending</c> after MT consume; the dispatcher
    /// flips it through <c>sending</c> while a delivery is in flight
    /// (post-claim) and removes the row on terminal success or moves
    /// it to <c>notification_dead_letter</c> on terminal failure.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>Number of delivery attempts so far (0 on initial insert).</summary>
    public int AttemptCount { get; set; }

    /// <summary>Timestamp of the most recent attempt; null until the first one runs.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Stable error code from the most recent attempt, if it failed.</summary>
    public string? LastErrorCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
