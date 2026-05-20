using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Persistence port for <c>password_reset_tokens</c> (Sprint-9 U3 ships
/// the EF impl). Stores only the SHA-256 hash of the plaintext token;
/// the plaintext is constructed in the forgot-password handler,
/// destroyed after the outbox emit, and never persisted server-side.
/// </summary>
/// <remarks>
/// Consume races converge via predicate-in-UPDATE: <c>UPDATE ... SET
/// used_at = now WHERE token_hash = @hash AND used_at IS NULL AND
/// expires_at &gt; now RETURNING user_id</c>. A second consumer for the
/// same token sees 0 rows affected and the handler collapses to the
/// canonical <c>auth.invalid_credentials</c> 401 (R6).
/// </remarks>
public interface IPasswordResetTokenRepository
{
    /// <summary>
    /// Insert a freshly-issued reset token (hash form only). Returns
    /// failure with code <c>auth.token_in_use</c> when the UNIQUE-23505
    /// fires on <c>token_hash</c> — astronomically rare but treated as
    /// a collision-retry hint for the caller.
    /// </summary>
    Task<Result> AddAsync(byte[] tokenHash, Guid userId, DateTime expiresAt, CancellationToken ct);

    /// <summary>
    /// Atomically consume a token. Returns success with the bound
    /// user id if the row was active (not yet used + not expired);
    /// returns failure with code <c>auth.invalid_token</c> otherwise.
    /// <paramref name="clock"/> provides the now-comparison; tests use
    /// <c>FakeTimeProvider</c>.
    /// </summary>
    Task<Result<Guid>> TryConsumeAsync(byte[] tokenHash, TimeProvider clock, CancellationToken ct);

    /// <summary>
    /// The most-recent issued-at timestamp for the user, or null when
    /// the user has never requested a reset. Used by the per-account
    /// cooldown gate (R32) before issuing a new token.
    /// </summary>
    Task<DateTime?> GetLastIssuedAtAsync(Guid userId, CancellationToken ct);
}
