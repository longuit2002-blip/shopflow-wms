namespace ShopFlow.Auth.Infrastructure.Tokens;

/// <summary>
/// Configuration knobs for the Sprint-8 U6
/// <see cref="JwtTokenIssuer"/>. Bound from the <c>Auth</c> config
/// section — the SAME section the kernel JwtBearer validator
/// (<c>AddShopFlowDefaults</c> in SharedKernel.Infrastructure) reads
/// from, so the issuer + validator stay coordinated without separate
/// secret rotation (KTD5).
/// </summary>
/// <remarks>
/// <para><see cref="Issuer"/> + <see cref="Audience"/> defaults match
/// the kernel validator defaults (<c>shopflow-dev</c> /
/// <c>shopflow-api</c>) and every existing module's
/// <c>appsettings.json</c>. Changing these requires a coordinated
/// update across all 7 modules (every Api project's appsettings)
/// AND any in-flight access tokens — schedule for a maintenance
/// window when the rename lands (the W6 split or a security
/// rotation event).</para>
///
/// <para><see cref="DevSecret"/> is REQUIRED. The kernel validator
/// throws at startup if <c>Auth:DevSecret</c> is missing; the issuer
/// here will also fail at first <c>IssueAccessToken</c> call if it's
/// empty (defense in depth).</para>
/// </remarks>
public sealed class JwtIssuerOptions
{
    public const string SectionName = "Auth";

    /// <summary>HMAC signing secret. Same key the kernel validator
    /// reads as <c>Auth:DevSecret</c>; must be at least 32 bytes
    /// when UTF-8 encoded for HS256 to be secure.</summary>
    public string DevSecret { get; set; } = string.Empty;

    /// <summary>JWT <c>iss</c> claim. Default <c>shopflow-dev</c>
    /// — matches every existing module appsettings + kernel
    /// validator default (KTD5).</summary>
    public string Issuer { get; set; } = "shopflow-dev";

    /// <summary>JWT <c>aud</c> claim. Default <c>shopflow-api</c>
    /// — matches kernel validator default (KTD5).</summary>
    public string Audience { get; set; } = "shopflow-api";

    /// <summary>Access-token lifetime in minutes. R11 — 15 min default
    /// keeps the bearer credential's blast radius small without
    /// requiring constant refresh.</summary>
    public int AccessTokenTtlMinutes { get; set; } = 15;
}
