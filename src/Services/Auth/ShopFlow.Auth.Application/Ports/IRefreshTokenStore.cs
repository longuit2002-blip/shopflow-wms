namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the Redis-backed refresh-token store (Sprint-8 U5 ships the
/// impl). Refresh tokens live in Redis — NOT in the per-tenant Postgres
/// DB — because per-tenant Redis namespacing
/// (<c>refresh:{tenantSlug}:{userId}:{tokenHash}</c>) sidesteps the
/// per-tenant-DB connection-storm cost on every refresh call (KTD3).
/// </summary>
/// <remarks>
/// <para>The Lua-scripted <see cref="RotateAsync"/> atomically (a)
/// validates the presented token, (b) deletes it, (c) issues a
/// successor, and (d) writes a 60-second tombstone keyed by the old
/// token hash. The tombstone exists so that legitimate concurrent
/// retries (client races, network glitches) do NOT trip the
/// reuse-detection lockout — both retries find the tombstone and
/// receive the SAME successor token, while a genuine replay against an
/// already-rotated token from minutes later still returns
/// <see cref="RefreshRotateOutcome.ReuseDetected"/> and triggers
/// session-wide revocation (R10 + ADV-002 mitigation).</para>
///
/// <para>All tenantSlug parameters are pre-validated against the
/// host-suffix allowlist + reserved-slug blocklist (U9 +
/// <c>SharedKernel.Infrastructure.ReservedSlugs</c>) before they reach
/// this port — the store does not re-validate.</para>
/// </remarks>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Issue a fresh refresh token for the user. Returns the opaque
    /// token string (URL-safe base64 of 32 random bytes); the store
    /// keeps only its SHA-256 hash. TTL is 7 days for normal sessions
    /// and 30 days when <paramref name="rememberMe"/> is <c>true</c>
    /// (R12).
    /// </summary>
    Task<string> IssueAsync(string tenantSlug, Guid userId, bool rememberMe, CancellationToken ct);

    /// <summary>
    /// Atomically rotate a presented refresh token to a new one.
    /// Outcome semantics:
    /// <list type="bullet">
    ///   <item><see cref="RefreshRotateOutcome.Issued"/> — token was
    ///     active; new token issued and old token tombstoned for 60s.</item>
    ///   <item><see cref="RefreshRotateOutcome.GraceReplay"/> — token
    ///     matches the tombstone window; same successor token returned
    ///     (legitimate concurrent retry, no revocation).</item>
    ///   <item><see cref="RefreshRotateOutcome.ReuseDetected"/> — token
    ///     was already rotated AND its tombstone expired; all sessions
    ///     for the user are revoked.</item>
    ///   <item><see cref="RefreshRotateOutcome.NotFound"/> — token has
    ///     no record; treat as invalid (expired, never issued, or
    ///     revoked).</item>
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
    /// password change, reuse-detection cascade, admin deactivation).
    /// Idempotent.
    /// </summary>
    Task RevokeAllForUserAsync(string tenantSlug, Guid userId, CancellationToken ct);
}

/// <summary>
/// Discriminated outcome of <see cref="IRefreshTokenStore.RotateAsync"/>.
/// The handler in U7 maps each case to a distinct HTTP response shape
/// (200 + new tokens / 200 + grace-replay tokens / 401 + revoke-all /
/// 401 + invalid).
/// </summary>
public enum RefreshRotateOutcome
{
    Issued,
    GraceReplay,
    ReuseDetected,
    NotFound,
}

/// <summary>
/// Result envelope for <see cref="IRefreshTokenStore.RotateAsync"/>.
/// <see cref="NewToken"/> is non-null for <see cref="RefreshRotateOutcome.Issued"/>
/// and <see cref="RefreshRotateOutcome.GraceReplay"/>; null otherwise.
/// </summary>
public sealed record RefreshRotateResult(RefreshRotateOutcome Outcome, string? NewToken);
