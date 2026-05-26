namespace ShopFlow.Contracts.Auth;

/// <summary>
/// Sprint-9 R30 — emitted when a user requests a password reset. The
/// Notification module consumes this + renders the email per KTD12
/// (workspace URL template). The Auth handler constructs the full
/// reset URL using <c>WorkspaceUrlTemplate</c> + plaintext token in
/// memory, then destroys the plaintext local variable before commit;
/// only the URL is persisted in this payload (doc-review P0 fix —
/// avoids a separate "PlaintextToken" field that integrators might
/// log as benign metadata).
/// </summary>
public sealed record PasswordResetRequestedV1(
    Guid TenantId,
    Guid UserId,
    string UserEmail,
    string TenantSlug,
    string ResetLinkUrl,
    DateTime ExpiresAtUtc,
    DateTime OccurredAtUtc,
    Guid CorrelationId
);
