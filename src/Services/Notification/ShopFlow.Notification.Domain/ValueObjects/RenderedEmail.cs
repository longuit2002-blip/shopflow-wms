namespace ShopFlow.Notification.Domain.ValueObjects;

/// <summary>
/// A fully-rendered transactional email. Produced by U2's
/// <c>ITemplateRenderer</c> from a template + variable dictionary and
/// handed off to U3's consumer for persistence in
/// <c>notification_outbox</c>. The dispatcher (U3) hands the same value
/// to <c>IMailerProvider</c> when claiming the row for delivery.
/// </summary>
/// <remarks>
/// <para><see cref="SourceEventId"/> is the load-bearing idempotency
/// anchor — the dispatcher's INSERT into <c>notification_log</c>
/// carries <c>UNIQUE(source_event_id, recipient_email)</c> (KTD3), so a
/// duplicate MT redelivery races a second outbox row but the second
/// dispatch fails on UNIQUE and the duplicate outbox row is dropped at
/// debug log level. The id MUST be the upstream Auth event id, not a
/// fresh GUID per render.</para>
/// <para><see cref="Subject"/> is capped at 998 octets per RFC 5322
/// §2.1.1; the renderer guards this upstream but the value object
/// re-checks so a hand-constructed instance can't smuggle an
/// overlength header line past the wire.</para>
/// </remarks>
public sealed class RenderedEmail
{
    /// <summary>RFC 5322 subject line. Already template-substituted.</summary>
    public string Subject { get; }

    /// <summary>Plain-text body. Already template-substituted.</summary>
    public string BodyText { get; }

    /// <summary>HTML body. Already template-substituted + HTML-escaped (KTD6).</summary>
    public string BodyHtml { get; }

    /// <summary>Upstream Auth event id (KTD3 idempotency anchor).</summary>
    public Guid SourceEventId { get; }

    private RenderedEmail(string subject, string bodyText, string bodyHtml, Guid sourceEventId)
    {
        Subject = subject;
        BodyText = bodyText;
        BodyHtml = bodyHtml;
        SourceEventId = sourceEventId;
    }

    /// <summary>
    /// Construct a rendered email. Trims subject whitespace and enforces
    /// the RFC 5322 998-octet subject ceiling; rejects null/empty
    /// subject + body + empty source-event id.
    /// </summary>
    /// <exception cref="ArgumentException">Any field is invalid.</exception>
    public static RenderedEmail Create(
        string? subject,
        string? bodyText,
        string? bodyHtml,
        Guid sourceEventId
    )
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException(
                "RenderedEmail subject must be non-empty.",
                nameof(subject)
            );
        }

        var trimmedSubject = subject.Trim();
        if (trimmedSubject.Length > 998)
        {
            throw new ArgumentException(
                "RenderedEmail subject exceeds 998-character RFC 5322 §2.1.1 ceiling.",
                nameof(subject)
            );
        }

        if (bodyText is null)
        {
            throw new ArgumentException(
                "RenderedEmail body_text must be non-null (use empty string for HTML-only mail).",
                nameof(bodyText)
            );
        }

        if (bodyHtml is null)
        {
            throw new ArgumentException(
                "RenderedEmail body_html must be non-null (use empty string for text-only mail).",
                nameof(bodyHtml)
            );
        }

        if (sourceEventId == Guid.Empty)
        {
            throw new ArgumentException(
                "RenderedEmail source_event_id must be non-empty — KTD3 idempotency UNIQUE requires the upstream Auth event id.",
                nameof(sourceEventId)
            );
        }

        return new RenderedEmail(trimmedSubject, bodyText, bodyHtml, sourceEventId);
    }
}
