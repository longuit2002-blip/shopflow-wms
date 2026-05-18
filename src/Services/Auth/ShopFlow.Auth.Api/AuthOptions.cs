namespace ShopFlow.Auth.Api;

/// <summary>
/// Configuration for the dev-mode fake login (Sprint-6 U4).
///
/// Bound from the <c>Auth</c> section in <c>appsettings.json</c> with overrides
/// from the <c>SHOPFLOW_AUTH__</c> environment variable namespace. All values
/// are dev-mode defaults; Sprint-7's real <c>JwtTokenIssuer</c> replaces this
/// surface with a Redis-backed denylist + signed refresh tokens.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>HMAC signing secret. Must be at least 32 bytes when UTF-8 encoded.</summary>
    public required string DevSecret { get; init; }

    /// <summary>JWT <c>iss</c> claim. Default: <c>shopflow-dev</c>.</summary>
    public string Issuer { get; init; } = "shopflow-dev";

    /// <summary>JWT <c>aud</c> claim. Default: <c>shopflow-api</c>.</summary>
    public string Audience { get; init; } = "shopflow-api";

    /// <summary>Token lifetime in seconds. Default: 3600 (1 hour).</summary>
    public int ExpiresInSeconds { get; init; } = 3600;

    /// <summary>
    /// Tenant slug baked into every issued token. Sprint-6 ships one demo
    /// tenant; Sprint-7's real login resolves the tenant from the user.
    /// </summary>
    public string DemoTenantSlug { get; init; } = "yensaokhanhhoa";

    /// <summary>
    /// Role string baked into every issued token. Sprint-6 ships the Owner
    /// vertical slice; the canonical claim value is <c>tenant_seller</c>
    /// (matches the brainstorm A1 Owner actor definition).
    /// </summary>
    public string DemoRole { get; init; } = "tenant_seller";
}
