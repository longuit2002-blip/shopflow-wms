namespace ShopFlow.Contracts.Auth;

/// <summary>
/// Sprint-9 R22 — emitted once on the lockout boundary attempt (e.g.
/// the 5th failure within the sliding window). Notification consumes
/// + fans out to Owner-role users for the tenant. Subsequent failures
/// while already locked do NOT re-emit.
/// </summary>
public sealed record AccountLockedV1(
    Guid TenantId,
    Guid UserId,
    string UserEmail,
    int FailedLoginCount,
    DateTime LockedUntilUtc,
    string SourceIp,
    DateTime OccurredAtUtc,
    Guid CorrelationId
);
