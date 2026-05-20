namespace ShopFlow.Contracts.Auth;

/// <summary>
/// Sprint-9 R39 — emitted on successful TOTP enrollment verify. The
/// Notification consumer sends a "MFA enabled on your account"
/// confirmation email to the user themselves (KTD R28 — this differs
/// from the Owner-fan-out shape of chain-reuse + account-locked).
/// </summary>
public sealed record MfaEnrolledV1(
    Guid TenantId,
    Guid UserId,
    string UserEmail,
    DateTime OccurredAtUtc,
    Guid CorrelationId);
