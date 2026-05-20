namespace ShopFlow.Auth.Infrastructure.Hashing;

/// <summary>
/// Configuration knobs for the Sprint-8 U4
/// <see cref="Argon2idPasswordHasher"/>. Bound from the
/// <c>Auth:Argon2</c> config section in U9's <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// <para>Defaults reflect the OWASP 2026 baseline for Argon2id:
/// 64 MB memory, 4 iterations, 4 lanes of parallelism. These are the
/// "high-quality option" from the OWASP Password Storage Cheat Sheet
/// (m=64 MiB, t=4, p=4). Production tenants with stronger latency
/// budgets can dial memory up via config; the PHC string baked at
/// hash time preserves whatever parameters were used so a future
/// parameter bump never invalidates existing rows.</para>
///
/// <para>Salt size is fixed at 16 bytes per the OWASP recommendation
/// (and the spec's minimum). It is not configurable — a smaller salt
/// is a footgun, a larger salt provides no meaningful additional
/// security for a per-row credential.</para>
/// </remarks>
public sealed class Argon2Options
{
    public const string SectionName = "Auth:Argon2";

    /// <summary>
    /// Memory size in KiB used by Argon2id. OWASP 2026 baseline = 65536
    /// (64 MiB). Higher is stronger but slower; do not drop below
    /// 19 MiB (the spec's "minimum for any application").
    /// </summary>
    public int MemorySizeKib { get; set; } = 65_536;

    /// <summary>
    /// Iteration count (time cost). OWASP 2026 baseline = 4. Increases
    /// CPU work linearly. The PHC string captures this value so future
    /// changes only affect newly-issued hashes.
    /// </summary>
    public int Iterations { get; set; } = 4;

    /// <summary>
    /// Degree of parallelism (number of lanes). OWASP 2026 baseline =
    /// 4. Should not exceed the number of CPU cores available to the
    /// hashing host.
    /// </summary>
    public int DegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Output hash length in bytes. OWASP 2026 baseline = 32 (256
    /// bits). The PHC encoding base64s this so the stored string is
    /// longer than 32 chars.
    /// </summary>
    public int HashLengthBytes { get; set; } = 32;
}
