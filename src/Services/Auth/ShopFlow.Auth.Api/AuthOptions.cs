namespace ShopFlow.Auth.Api;

/// <summary>
/// Auth-module API configuration (Sprint-8 U9). Bound from the
/// <c>Auth</c> section in <c>appsettings.json</c> with overrides from
/// the <c>SHOPFLOW_AUTH__</c> environment variable namespace.
/// </summary>
/// <remarks>
/// <para>The <c>DevSecret</c>, <c>Issuer</c>, <c>Audience</c> fields
/// double as the kernel JwtBearer validator config + the
/// <c>JwtTokenIssuer</c> input (KTD5 — single source of truth for the
/// iss/aud/key triple). Default values match the kernel validator
/// defaults so every existing module's appsettings need no migration.</para>
///
/// <para><see cref="TrustedHostSuffixes"/> is the host-suffix allowlist
/// the Auth.Api's in-controller subdomain resolver checks BEFORE
/// extracting the slug (post-doc-review SEC-004 — hard requirement
/// to prevent Host-header injection attacks).</para>
/// </remarks>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>HMAC signing secret. Must be at least 32 bytes when
    /// UTF-8 encoded. SAME key the kernel JwtBearer validator reads.</summary>
    public required string DevSecret { get; init; }

    /// <summary>JWT <c>iss</c> claim. Default <c>shopflow-dev</c>
    /// matches every module's appsettings + the kernel validator's
    /// default.</summary>
    public string Issuer { get; init; } = "shopflow-dev";

    /// <summary>JWT <c>aud</c> claim. Default <c>shopflow-api</c>.</summary>
    public string Audience { get; init; } = "shopflow-api";

    /// <summary>Host-suffix allowlist for the in-controller subdomain
    /// resolver. Default <c>shopflow.com</c> / <c>shopflow.local</c> /
    /// <c>localhost</c> covers prod + dev hosts. Add tenant-test
    /// domains via configuration in dev.</summary>
    public IReadOnlyList<string> TrustedHostSuffixes { get; init; } =
        new[] { "shopflow.com", "shopflow.local", "localhost" };
}
