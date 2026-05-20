namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Append-only persistence port for <c>auth_audit_log</c> (Sprint-9 U3
/// ships the EF impl). Writes are fire-and-forget from the handler
/// perspective — the calling auth path returns its response without
/// waiting on the audit insert completing.
/// </summary>
/// <remarks>
/// <para>Sprint-9 emits the following <c>EventType</c> values (15
/// distinct keys per R42):
/// <c>auth.login.success</c>, <c>auth.login.failed</c>,
/// <c>auth.login.locked</c>, <c>auth.refresh.success</c>,
/// <c>auth.refresh.reused</c>, <c>auth.logout</c>,
/// <c>auth.password.changed</c>, <c>auth.password.reset.requested</c>,
/// <c>auth.password.reset.completed</c>, <c>auth.mfa.enrolled</c>,
/// <c>auth.mfa.used</c>, <c>auth.mfa.disabled</c>,
/// <c>auth.mfa.reset_by_owner</c>, <c>auth.account.unlocked_by_owner</c>,
/// <c>auth.role_permissions.changed</c>.</para>
///
/// <para>Sprint-10+ adds partitioning + archival. Sprint-9 ships one
/// unpartitioned table to keep the migration footprint small.</para>
/// </remarks>
public interface IAuthAuditLogRepository
{
    /// <summary>
    /// Append a single audit row. <paramref name="userId"/> is nullable
    /// because some events (failed login with unknown email) have no
    /// resolved user. <paramref name="metadataJson"/> is opaque JSON
    /// the consumer can shape per event type — keeps the schema stable
    /// while the event payloads evolve.
    /// </summary>
    Task AppendAsync(
        string eventType,
        Guid? userId,
        string sourceIp,
        string userAgent,
        string metadataJson,
        Guid correlationId,
        CancellationToken ct);
}
