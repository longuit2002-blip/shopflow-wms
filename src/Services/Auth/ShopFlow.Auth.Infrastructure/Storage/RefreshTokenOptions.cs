namespace ShopFlow.Auth.Infrastructure.Storage;

/// <summary>
/// Configuration knobs for the Sprint-8 U5
/// <see cref="RedisRefreshTokenStore"/>. Bound from the
/// <c>Auth:Refresh</c> config section in U9's <c>Program.cs</c>.
/// </summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "Auth:Refresh";

    /// <summary>
    /// Standard refresh-token TTL in days (R12). 7 days mirrors common
    /// SaaS norms — short enough that a stolen refresh window is
    /// bounded, long enough that operators on weekly schedules don't
    /// re-login every shift.
    /// </summary>
    public int RefreshTtlDays { get; set; } = 7;

    /// <summary>
    /// Extended TTL in days when the user opts into "remember me"
    /// at login (R12 — out-of-scope explicit add). 30 days is the
    /// conservative-extended pick; anything beyond 90 days starts to
    /// erode the security benefit of refresh rotation.
    /// </summary>
    public int RememberMeTtlDays { get; set; } = 30;

    /// <summary>
    /// Grace window in seconds during which a just-rotated token can
    /// be presented again (legitimate concurrent retry by a multi-tab
    /// browser or a flaky-network client) and receive the SAME
    /// successor token rather than tripping reuse-detection lockout
    /// (KTD3 + ADV-002 mitigation). 60s is the OWASP refined-pattern
    /// recommendation — long enough to absorb realistic retry/race
    /// patterns, short enough that a true theft-then-replay window
    /// stays narrow.
    /// </summary>
    public int RotationGraceWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Redis connection string. Override via the
    /// <c>ConnectionStrings:Redis</c> binding in <c>Program.cs</c> per
    /// the host-side IConfiguration convention; this default is a
    /// dev-only fallback for tests that build the options directly.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";
}
