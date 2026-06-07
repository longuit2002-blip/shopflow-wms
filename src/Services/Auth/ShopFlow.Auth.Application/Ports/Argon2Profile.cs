namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Sprint-9 KTD9 — Argon2id parameter profile selection. The PHC string
/// embeds the parameters so <see cref="IPasswordHasher.Verify"/> never
/// needs to know which profile produced the hash; the profile only
/// matters at <see cref="IPasswordHasher.Hash"/> time.
/// </summary>
public enum Argon2Profile
{
    /// <summary>
    /// OWASP 2026 password profile (m=64 MiB, t=4, p=4). ~250-500 ms
    /// wall-time on commodity hardware — appropriate for password
    /// hashing where the work factor is the security budget.
    /// </summary>
    Password = 0,

    /// <summary>
    /// Recovery-code profile (m=8 MiB, t=2, p=1). ~20-50 ms wall-time.
    /// Recovery codes carry ~52 bits of entropy, so the
    /// password-grade work factor is excessive; 10 codes × full profile
    /// would balloon enrollment cost to ~5 seconds. The lighter profile
    /// preserves a meaningful brute-force barrier without that latency.
    /// </summary>
    RecoveryCode = 1,
}
