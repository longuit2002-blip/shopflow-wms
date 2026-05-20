namespace ShopFlow.Auth.Application;

/// <summary>
/// Sprint-9 U8 lockout policy knobs. Bound from <c>Auth:Lockout</c>.
/// Defaults: 5 fails in 15 min triggers a 15-min lockout (per R18 /
/// NIST SP 800-63B-4 + OWASP Authentication Cheat Sheet).
/// </summary>
public sealed class AuthLockoutOptions
{
    public const string SectionName = "Auth:Lockout";

    public int MaxAttempts { get; set; } = 5;

    public int WindowMinutes { get; set; } = 15;

    public int DurationMinutes { get; set; } = 15;
}

/// <summary>
/// Sprint-9 password-reset policy knobs. Bound from
/// <c>Auth:PasswordReset</c>.
/// </summary>
public sealed class AuthPasswordResetOptions
{
    public const string SectionName = "Auth:PasswordReset";

    /// <summary>Per-user cooldown between successive reset requests.</summary>
    public int CooldownMinutes { get; set; } = 5;

    /// <summary>TTL on the reset token itself.</summary>
    public int TokenTtlMinutes { get; set; } = 30;

    /// <summary>
    /// Workspace URL template — the link in the reset email is
    /// <c>{TemplateBase}/reset-password?token=&lt;plaintext&gt;</c> where
    /// <c>{tenant_slug}</c> is substituted at format time. KTD12.
    /// </summary>
    public string WorkspaceUrlTemplate { get; set; } = "https://{slug}.shopflow.local";

    /// <summary>
    /// Sentinel hash used for constant-time response when the email is
    /// unknown. The forgot-password handler runs a dummy
    /// <see cref="Ports.IPasswordHasher.Verify"/> against this so the
    /// wall-time of the unknown-email path matches the matched-email
    /// path (KTD14). Generate via Argon2id of a random plaintext at
    /// deploy time + paste into appsettings.
    /// </summary>
    public string SyntheticHash { get; set; } =
        "$argon2id$v=19$m=65536,t=4,p=4$c2VudGluZWxzYWx0$c2VudGluZWxoYXNoZmFsbGJhY2s";
}
