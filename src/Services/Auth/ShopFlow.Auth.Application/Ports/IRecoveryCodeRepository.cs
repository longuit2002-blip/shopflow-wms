using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Persistence port for <c>user_recovery_codes</c> (Sprint-9 U3 ships
/// the EF impl). Hashed Argon2id-RecoveryCode-profile entries; the
/// plaintext is shown to the user ONCE at enrollment / regenerate and
/// is never recoverable.
/// </summary>
/// <remarks>
/// Single-use is enforced by the consume predicate
/// <c>UPDATE ... SET used_at = now WHERE user_id = @u AND code_hash =
/// @h AND used_at IS NULL RETURNING 1</c>. A second consume returns
/// <c>false</c>; AE3 pins this behaviour at the integration-test layer
/// in U16.
/// </remarks>
public interface IRecoveryCodeRepository
{
    /// <summary>
    /// Insert a freshly-generated batch of hashes. Returns failure with
    /// code <c>auth.recovery_codes_in_use</c> when a UNIQUE-23505 fires
    /// (collision on <c>(user_id, code_hash)</c>) — the handler retries
    /// with a fresh batch.
    /// </summary>
    Task<Result> AddBatchAsync(Guid userId, IReadOnlyList<string> phcHashes, CancellationToken ct);

    /// <summary>
    /// Attempt to consume any recovery code for a user that matches
    /// the given plaintext after applying
    /// <see cref="IPasswordHasher.Verify"/> against the stored PHC
    /// hashes. Returns <c>true</c> on successful single-use consume.
    /// Implementation iterates active codes inside one tracked
    /// transaction — a brute-force attacker still has to defeat
    /// Argon2id RecoveryCode-profile work-factor per code.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid userId, string plaintext, IPasswordHasher hasher, CancellationToken ct);

    /// <summary>
    /// Count of active (non-consumed, non-expired) recovery codes for
    /// the user. Surfaces in the profile-security UI + MFA-verify
    /// response so the user knows when to regenerate.
    /// </summary>
    Task<int> CountRemainingAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Delete every recovery code row for the user. Called before a
    /// fresh batch is inserted (regenerate) and on Owner-driven
    /// MFA reset.
    /// </summary>
    Task DeleteAllAsync(Guid userId, CancellationToken ct);
}
