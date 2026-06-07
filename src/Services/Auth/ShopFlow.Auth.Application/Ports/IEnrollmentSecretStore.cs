namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the short-TTL Redis store that holds in-flight TOTP
/// enrollment secrets (Sprint-9 U4 ships the impl). The secret lives
/// for 10 minutes per KTD10; on successful verify it migrates to
/// durable storage via <see cref="ITotpSecretRepository.UpsertAsync"/>.
/// </summary>
/// <remarks>
/// <para>Key shape:
/// <c>auth:totpenroll:{tenantSlug}:{userId}:{enrollmentId}</c>. The
/// enrollment_id is a fresh Guid the begin-enroll handler returns to
/// the client; the verify handler echoes it back, binding the verify
/// call to the issued secret.</para>
///
/// <para>NOT in JWT (URL/log leak). NOT in process memory (the
/// modular monolith may run multiple instances under Aspire and
/// rolling restarts must not drop in-flight enrollments).</para>
/// </remarks>
public interface IEnrollmentSecretStore
{
    /// <summary>
    /// Store an enrollment secret with a 10-min TTL. Returns the
    /// freshly-minted enrollment id the caller hands back to the
    /// client (URL-safe). Throws on Redis I/O failure — the caller
    /// converts to <c>auth.invalid_credentials</c> + 401.
    /// </summary>
    Task<Guid> StoreAsync(string tenantSlug, Guid userId, byte[] secret, CancellationToken ct);

    /// <summary>
    /// Atomic GET + DEL. Returns null when the key is missing
    /// (expired or replay-of-already-consumed). The verify handler
    /// treats null as <c>auth.invalid_credentials</c>.
    /// </summary>
    Task<byte[]?> ConsumeAsync(
        string tenantSlug,
        Guid userId,
        Guid enrollmentId,
        CancellationToken ct
    );
}
