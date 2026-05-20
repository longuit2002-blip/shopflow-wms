namespace ShopFlow.Auth.Infrastructure.Hashing;

/// <summary>
/// Configuration knobs for <see cref="Argon2idPasswordHasher"/>. Bound
/// from the <c>Auth:Argon2</c> config section in U9's <c>Program.cs</c>.
/// The Sprint-8 flat fields stay at the root for back-compat (the
/// Password profile); Sprint-9 adds a nested <see cref="RecoveryCode"/>
/// block for the lighter profile per KTD9.
/// </summary>
/// <remarks>
/// <para>Defaults reflect OWASP 2026: Password = 64 MiB memory, 4
/// iterations, 4 lanes; RecoveryCode = 8 MiB, 2 iterations, 1 lane (10
/// codes × full Password profile would balloon enrollment cost to ~5
/// seconds, so the lighter profile is required).</para>
///
/// <para>The PHC string parameter-embedding lets a single
/// <c>Verify(plaintext, phc)</c> call work across profiles without
/// knowing which profile produced the hash.</para>
/// </remarks>
public sealed class Argon2Options
{
    public const string SectionName = "Auth:Argon2";

    // -------- Sprint-8 / Password profile (flat at section root for back-compat) --------

    /// <summary>Password-profile memory size in KiB. OWASP 2026 = 65536 (64 MiB).</summary>
    public int MemorySizeKib { get; set; } = 65_536;

    /// <summary>Password-profile iteration count. OWASP 2026 = 4.</summary>
    public int Iterations { get; set; } = 4;

    /// <summary>Password-profile degree of parallelism. OWASP 2026 = 4.</summary>
    public int DegreeOfParallelism { get; set; } = 4;

    /// <summary>Password-profile output hash length in bytes. OWASP 2026 = 32.</summary>
    public int HashLengthBytes { get; set; } = 32;

    // -------- Sprint-9 RecoveryCode profile (nested block) --------

    /// <summary>
    /// Recovery-code profile (Sprint-9 KTD9). Lighter parameters
    /// because the codes themselves carry ~52-bit entropy.
    /// </summary>
    public Argon2ProfileSettings RecoveryCode { get; set; } = new()
    {
        MemorySizeKib = 8_192,
        Iterations = 2,
        DegreeOfParallelism = 1,
        HashLengthBytes = 32,
    };
}

/// <summary>
/// Per-profile parameter block. Mirrors the Sprint-8 top-level flat
/// fields so binding from a config sub-section "just works".
/// </summary>
public sealed class Argon2ProfileSettings
{
    public int MemorySizeKib { get; set; }

    public int Iterations { get; set; }

    public int DegreeOfParallelism { get; set; }

    public int HashLengthBytes { get; set; }
}
