namespace ShopFlow.Notification.Domain.ValueObjects;

/// <summary>
/// In-process metadata describing the destination of a transactional email.
/// Carried on the rendering boundary between Sprint-9 cross-module event
/// payloads and the <see cref="RenderedEmail"/> handed to
/// <c>IMailerProvider</c> (U2). Not persisted as a row of its own —
/// <see cref="Email"/> + <see cref="DisplayName"/> land as columns on
/// <c>notification_outbox</c>; <see cref="TenantId"/> is dispatcher-side
/// logging metadata only (per ADR-0003 the DB identity IS the tenant
/// boundary; no tenant column on any Notification table).
/// </summary>
public sealed class Recipient
{
    /// <summary>Lowercase-normalised RFC 5322 email address.</summary>
    public string Email { get; }

    /// <summary>Optional display name; null when only the address is known.</summary>
    public string? DisplayName { get; }

    /// <summary>Tenant the recipient belongs to (dispatcher logging metadata; never persisted).</summary>
    public Guid TenantId { get; }

    private Recipient(string email, string? displayName, Guid tenantId)
    {
        Email = email;
        DisplayName = displayName;
        TenantId = tenantId;
    }

    /// <summary>
    /// Construct a recipient. Trims surrounding whitespace and lower-cases
    /// the email; rejects null / empty / overlength input (RFC 5321 caps
    /// the local-part at 64 and total at 254 octets, so 254 is the
    /// outer wall).
    /// </summary>
    /// <exception cref="ArgumentException">Email or tenant is invalid.</exception>
    public static Recipient Create(string? email, string? displayName, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Recipient email must be non-empty.", nameof(email));
        }

        var trimmed = email.Trim().ToLowerInvariant();
        if (trimmed.Length > 254)
        {
            throw new ArgumentException(
                "Recipient email exceeds 254-character RFC 5321 limit.",
                nameof(email)
            );
        }
        if (trimmed.IndexOf('@', StringComparison.Ordinal) < 1)
        {
            throw new ArgumentException(
                "Recipient email must contain an @-sign with a non-empty local-part.",
                nameof(email)
            );
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Recipient tenant id must not be empty.", nameof(tenantId));
        }

        var displayTrimmed = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        return new Recipient(trimmed, displayTrimmed, tenantId);
    }
}
