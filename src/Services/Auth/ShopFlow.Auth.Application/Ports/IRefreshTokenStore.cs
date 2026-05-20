namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the Redis-backed refresh-token store (Sprint-8 U5 ships the
/// initial impl; Sprint-9 U5 extends with chain-aware reuse detection +
/// 7-day tombstone TTL). Refresh tokens live in Redis — NOT in the
/// per-tenant Postgres DB — because per-tenant Redis namespacing
/// (<c>refresh:{tenantSlug}:{userId}:{tokenHash}</c>) sidesteps the
/// per-tenant-DB connection-storm cost on every refresh call.
/// </summary>
/// <remarks>
/// <para>Sprint-9 chain semantics: each login mints a fresh
/// <c>chain_id</c>; rotation carries the chain_id from predecessor to
/// successor; reuse-detection (post-grace replay) calls
/// <see cref="RevokeChainAsync"/> to wipe just that chain's tokens
/// (KTD2 — chain-only revoke, not all-user-sessions, per RFC 9700
/// §4.14 + Auth0/Okta 2026 production canon).</para>
///
/// <para>Tombstone TTL extends from 60 sec (Sprint-8) to 7 days (KTD3)
/// to match refresh-token TTL. The grace-window check remains a
/// code-level <c>now - rotated_at &lt; RotationGraceWindowSeconds</c>
/// comparison; the longer TTL is the durable window in which the store
/// can still detect a post-grace replay attempt against the
/// already-rotated token hash and trigger chain revocation.</para>
///
/// <para>Sprint-9 U2 keeps the Sprint-8 <see cref="IssueAsync"/> +
/// <see cref="RotateAsync"/> signatures intact (additive new methods +
/// new <see cref="RefreshRotateOutcome.ChainRevoked"/> enum case
/// only). U5 swaps the Redis impl to carry chain_id through the
/// rotation chain.</para>
/// </remarks>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Issue a fresh refresh token for the user. Returns the opaque
    /// token string (URL-safe base64 of 32 random bytes); the store
    /// keeps only its SHA-256 hash. TTL is 7 days for normal sessions
    /// and 30 days when <paramref name="rememberMe"/> is <c>true</c>
    /// (R12). Sprint-9 U5 mints a fresh <c>chain_id</c> bound to the
    /// new token; the chain_id is internal to the store and surfaces
    /// to handlers only via <see cref="RefreshRotateResult.ChainId"/>
    /// on rotation.
    /// </summary>
    Task<string> IssueAsync(string tenantSlug, Guid userId, bool rememberMe, CancellationToken ct);

    /// <summary>
    /// Atomically rotate a presented refresh token to a new one.
    /// Outcome semantics:
    /// <list type="bullet">
    ///   <item><see cref="RefreshRotateOutcome.Issued"/> — token was
    ///     active; new token issued and predecessor tombstoned for 7
    ///     days carrying chain_id + cached successor plaintext +
    ///     rotated_at.</item>
    ///   <item><see cref="RefreshRotateOutcome.GraceReplay"/> — token
    ///     matches the tombstone within the 60-sec grace window; same
    ///     successor plaintext returned (legitimate concurrent retry,
    ///     no revocation).</item>
    ///   <item><see cref="RefreshRotateOutcome.ChainRevoked"/> — token
    ///     matches the tombstone but the grace window has elapsed;
    ///     every token in the chain is revoked and the handler emits
    ///     <c>RefreshReuseDetectedV1</c>. Sprint-9 new outcome.</item>
    ///   <item><see cref="RefreshRotateOutcome.ReuseDetected"/> —
    ///     Sprint-8 legacy outcome (revoked all user sessions). Kept
    ///     for compatibility during the rolling Sprint-9 deploy;
    ///     Sprint-9 U5 store impls return <see cref="RefreshRotateOutcome.ChainRevoked"/>
    ///     instead.</item>
    ///   <item><see cref="RefreshRotateOutcome.NotFound"/> — token has
    ///     no record; treat as invalid.</item>
    /// </list>
    /// </summary>
    Task<RefreshRotateResult> RotateAsync(
        string tenantSlug,
        Guid userId,
        string presentedToken,
        CancellationToken ct);

    /// <summary>
    /// Revoke a single refresh token (logout from one device).
    /// Idempotent: a missing key is a no-op success.
    /// </summary>
    Task RevokeAsync(string tenantSlug, Guid userId, string token, CancellationToken ct);

    /// <summary>
    /// Revoke every refresh token for the user (logout-all-devices,
    /// password change, password reset, admin deactivation). Idempotent.
    /// </summary>
    Task RevokeAllForUserAsync(string tenantSlug, Guid userId, CancellationToken ct);

    /// <summary>
    /// Revoke every live token in a single rotation chain. Called by
    /// <see cref="RotateAsync"/> when post-grace replay is detected so
    /// other devices on independent chains for the same user keep
    /// working (KTD2). Idempotent.
    /// </summary>
    Task RevokeChainAsync(string tenantSlug, Guid userId, Guid chainId, CancellationToken ct);
}

/// <summary>
/// Discriminated outcome of <see cref="IRefreshTokenStore.RotateAsync"/>.
/// </summary>
public enum RefreshRotateOutcome
{
    /// <summary>Token was active; new successor issued.</summary>
    Issued,

    /// <summary>Token matched tombstone within 60-sec grace; same successor returned.</summary>
    GraceReplay,

    /// <summary>Sprint-8 legacy — revoked all user sessions.</summary>
    ReuseDetected,

    /// <summary>Token has no record. Treat as invalid (expired or revoked).</summary>
    NotFound,

    /// <summary>
    /// Sprint-9 — token matched tombstone post-grace. Store has
    /// already invoked <see cref="IRefreshTokenStore.RevokeChainAsync"/>
    /// for the bound chain_id. Handler emits
    /// <c>RefreshReuseDetectedV1</c> + 401 <c>auth.refresh_reused</c>.
    /// </summary>
    ChainRevoked,
}

/// <summary>
/// Result envelope for <see cref="IRefreshTokenStore.RotateAsync"/>.
/// <see cref="NewToken"/> is non-null for
/// <see cref="RefreshRotateOutcome.Issued"/> and
/// <see cref="RefreshRotateOutcome.GraceReplay"/>; null otherwise.
/// <see cref="ChainId"/> is populated for every outcome except
/// <see cref="RefreshRotateOutcome.NotFound"/> so the handler can
/// reference the revoked chain in the emitted reuse-detection event.
/// Sprint-8 callers ignored <see cref="ChainId"/>; Sprint-9 U8
/// RefreshTokenCommandHandler reads it for the
/// <c>RefreshReuseDetectedV1</c> payload.
/// </summary>
public sealed record RefreshRotateResult(
    RefreshRotateOutcome Outcome,
    string? NewToken,
    Guid? ChainId = null);
