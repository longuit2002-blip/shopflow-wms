namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Persistence port for <c>user_totp_secrets</c> (Sprint-9 U3 ships the
/// EF impl). One row per enrolled user; the row stores the AES-256-GCM
/// encrypted secret + the key id used for that encryption so KEK
/// rotation can be lazy (read-Current-fallback-Previous per KTD8).
/// </summary>
public interface ITotpSecretRepository
{
    /// <summary>
    /// Read the encrypted secret + key id for a user, plus the
    /// <c>last_used_step</c> bookkeeping that prevents within-window
    /// OTP replay. Returns null when no enrollment row exists.
    /// </summary>
    Task<TotpSecretView?> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Insert or replace the user's TOTP enrollment row. Called by the
    /// MFA enroll-verify handler after the first OTP verifies — at
    /// that point the secret is committed to durable storage.
    /// </summary>
    Task UpsertAsync(
        Guid userId,
        byte[] encryptedSecret,
        int keyId,
        long? lastUsedTimeStep,
        CancellationToken ct);

    /// <summary>
    /// Record the latest verified TOTP step so a subsequent verify with
    /// the same step is rejected (prevents within-window replay).
    /// </summary>
    Task UpdateLastUsedStepAsync(Guid userId, long timeStep, CancellationToken ct);

    /// <summary>
    /// Delete the enrollment row. Called by self-service disable +
    /// Owner-driven MFA reset.
    /// </summary>
    Task DeleteAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Read projection of <c>user_totp_secrets</c>. Carries the encrypted
/// blob exactly as it lives in the DB; the caller invokes
/// <see cref="ITotpSecretCipher.Decrypt"/> with the matching key id to
/// recover the plaintext for OTP verification.
/// </summary>
public sealed record TotpSecretView(
    byte[] EncryptedSecret,
    int KeyId,
    long? LastUsedTimeStep);
