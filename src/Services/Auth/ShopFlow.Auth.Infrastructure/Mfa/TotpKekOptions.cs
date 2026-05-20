namespace ShopFlow.Auth.Infrastructure.Mfa;

/// <summary>
/// Sprint-9 U4 / KTD8 — env-var-sourced KEK for the TOTP secret
/// envelope cipher. Bound from <c>Auth:TotpKek</c> in U9. Both
/// <see cref="Current"/> and <see cref="Previous"/> are base64-encoded
/// 32-byte AES-256 keys.
/// </summary>
/// <remarks>
/// <para>Rotation cadence (Sprint-10+ ops work — Scope Boundary):
/// (1) generate a fresh 32-byte key; (2) move existing
/// <see cref="Current"/> to <see cref="Previous"/>, bump
/// <see cref="CurrentKeyId"/>; (3) deploy; (4) the read path falls
/// back to Previous when a row's stored <c>totp_key_id</c> doesn't
/// match Current; (5) optional background sweep re-encrypts
/// Previous-encrypted rows.</para>
///
/// <para>Sprint-9 KTD8 picks env-var KEK acceptable for small-mid SaaS
/// scale per OWASP Cryptographic Storage Cheat Sheet. KMS / Vault
/// integration is Sprint-10+.</para>
/// </remarks>
public sealed class TotpKekOptions
{
    public const string SectionName = "Auth:TotpKek";

    /// <summary>Base64-encoded 32-byte Current KEK. Required.</summary>
    public string Current { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded 32-byte Previous KEK (rotation slot). Optional —
    /// when null/empty, the read fallback path returns null instead of
    /// attempting to decrypt with a missing key.
    /// </summary>
    public string? Previous { get; set; }

    /// <summary>
    /// Key id stamped into <c>user_totp_secrets.totp_key_id</c> on
    /// every new encrypt. Bumps when KEK rotates so the lazy read
    /// fallback knows which key to use.
    /// </summary>
    public int CurrentKeyId { get; set; } = 1;
}
