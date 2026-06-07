namespace ShopFlow.Contracts.Auth;

/// <summary>
/// Sprint-9 R28 — emitted when chain-aware refresh-token reuse
/// detection fires (post-grace replay → store revokes the chain).
/// Notification consumes + emails Owner-role users for the tenant per
/// KTD15 (user's brainstorm preference; Sprint-10+ stretch also emails
/// the affected user per OWASP Session Management Cheat Sheet).
/// </summary>
public sealed record RefreshReuseDetectedV1(
    Guid TenantId,
    Guid UserId,
    string AffectedUserEmail,
    Guid ChainId,
    string PresentedTokenHash,
    string PresentingIp,
    string UserAgent,
    DateTime OccurredAtUtc,
    Guid CorrelationId
);
