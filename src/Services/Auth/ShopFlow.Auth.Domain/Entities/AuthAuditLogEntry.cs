namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Sprint-9 U3 / R41-R43 — append-only audit row for the
/// <c>auth_audit_log</c> table. Captures every auth-relevant event
/// (login success/failure/lockout, refresh, MFA enroll/use/disable,
/// password reset, role-permissions change). Sprint-10+ adds
/// partitioning + archival; Sprint-9 ships a single unpartitioned table.
/// </summary>
public sealed class AuthAuditLogEntry
{
    public long Id { get; private set; }

    public string EventType { get; private set; } = default!;

    public Guid? UserId { get; private set; }

    public string SourceIp { get; private set; } = default!;

    public string UserAgent { get; private set; } = default!;

    public string MetadataJson { get; private set; } = default!;

    public Guid CorrelationId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private AuthAuditLogEntry() { }

    public static AuthAuditLogEntry Record(
        string eventType,
        Guid? userId,
        string sourceIp,
        string userAgent,
        string metadataJson,
        Guid correlationId,
        DateTime now
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return new AuthAuditLogEntry
        {
            EventType = eventType,
            UserId = userId,
            SourceIp = sourceIp ?? string.Empty,
            UserAgent = userAgent ?? string.Empty,
            MetadataJson = metadataJson ?? "{}",
            CorrelationId = correlationId,
            OccurredAt = now,
        };
    }
}
