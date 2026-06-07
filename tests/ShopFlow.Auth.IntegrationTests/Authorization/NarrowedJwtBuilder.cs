using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ShopFlow.Auth.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — narrowed-JWT builder for the 33 + 1 403 wire-shape
/// integration tests across Inventory / Outbound / Inbound / AuthAdmin.
///
/// <para>Given a <c>tenantSlug</c>, a <c>userId</c>, and a set of
/// permission keys to include, produces a signed HS256 access token
/// whose claim shape mirrors <c>ShopFlow.Auth.Infrastructure.Tokens.JwtTokenIssuer</c>
/// (Sprint-8 U6 / Sprint-9 U6): <c>sub</c>, <c>email</c>, <c>role</c>,
/// <c>tenant_slug</c>, <c>iss</c>, <c>aud</c>, <c>iat</c>, <c>exp</c>,
/// plus one <c>Claim("perm", value)</c> per key (Sprint-9 KTD1 —
/// <c>JsonWebTokenHandler</c> array-flattens identical-type claims).</para>
///
/// <para>Tests inject a <c>perm[]</c> set that <em>omits</em> the action's
/// required key; the per-action <c>[Authorize(Policy = PermissionKeys.X)]</c>
/// gate rejects the request with 403. Mirrors the live issuer's signing
/// (HS256 via <c>SymmetricSecurityKey</c> over UTF-8 <c>DevSecret</c>
/// bytes) so the kernel <c>JwtBearer</c> validator wired by
/// <c>AddShopFlowDefaults</c> accepts the token's <em>authentication</em>
/// while the policy engine independently rejects on <em>authorization</em>.</para>
///
/// <para>Shared infrastructure (project-referenced from each module's
/// <c>*AuthorizationFixture</c>) — keeps the 33 + 1 tests trivial: arrange
/// a narrowed perm set, build the JWT, send the request, assert 403.</para>
/// </summary>
public sealed class NarrowedJwtBuilder
{
    private readonly string _devSecret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _accessTokenTtl;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SymmetricSecurityKey _signingKey;

    /// <summary>
    /// Build a narrowed-JWT signer. Defaults align with the test
    /// fixtures' <c>Auth:DevSecret</c> + <c>Auth:Issuer</c> +
    /// <c>Auth:Audience</c> overrides.
    /// </summary>
    /// <param name="devSecret">Shared HS256 signing secret. MUST match
    /// the value injected into the fixture's
    /// <c>WebApplicationFactory&lt;Program&gt;</c> via
    /// <c>UseSetting("Auth:DevSecret", ...)</c>. Minimum 32 UTF-8 bytes
    /// per the kernel validator's hard rule.</param>
    /// <param name="issuer">Expected <c>iss</c> claim — matches the
    /// kernel validator's <c>ValidIssuer</c>.</param>
    /// <param name="audience">Expected <c>aud</c> claim — matches the
    /// kernel validator's <c>ValidAudience</c>.</param>
    /// <param name="accessTokenTtl">Token lifetime. Default 15 min
    /// mirrors <c>JwtIssuerOptions.AccessTokenTtlMinutes</c>.</param>
    public NarrowedJwtBuilder(
        string devSecret,
        string issuer = "shopflow-dev",
        string audience = "shopflow-api",
        TimeSpan? accessTokenTtl = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        var keyBytes = Encoding.UTF8.GetBytes(devSecret);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:DevSecret must be at least 32 bytes (UTF-8) for HS256. "
                    + "The kernel JwtBearer validator enforces the same minimum; "
                    + "an undersize key here would silently produce tokens the validator rejects."
            );
        }

        _devSecret = devSecret;
        _issuer = issuer;
        _audience = audience;
        _accessTokenTtl = accessTokenTtl ?? TimeSpan.FromMinutes(15);
        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    /// <summary>
    /// Build a signed access token carrying the Sprint-9 standard claim
    /// shape plus one <c>Claim("perm", key)</c> entry per <paramref name="includeKeys"/>.
    /// Per Sprint-9 KTD1, <see cref="JsonWebTokenHandler"/> serializes
    /// identical-type claims into a JSON <c>string[]</c> under the
    /// <c>perm</c> name; the policy engine's <c>RequireClaim("perm", key)</c>
    /// matches one element at a time.
    /// </summary>
    /// <param name="tenantSlug">Resolved tenant slug — emitted as the
    /// <c>tenant_slug</c> claim. Consumed by
    /// <c>TenantRoutingMiddleware</c> for per-request DbContext binding.</param>
    /// <param name="userId">Subject identifier — emitted as <c>sub</c>.</param>
    /// <param name="includeKeys">The keys to grant. Tests pass
    /// <c>PermissionKeys.All.Where(k =&gt; k != actionKey).ToArray()</c>
    /// so the action's required key is the <em>only</em> one missing
    /// from <c>perm[]</c> — proves the 403 was caused by that key
    /// specifically, not by an unrelated absence.</param>
    /// <param name="email">Optional <c>email</c> claim. Defaults to a
    /// deterministic test email.</param>
    /// <param name="role">Optional <c>role</c> claim. Defaults to
    /// <c>"Owner"</c>; narrowing <c>perm[]</c> is the orthogonal axis
    /// that drives the 403 — role values are accepted unchanged by the
    /// per-action policy engine post Sprint-10 U4 (the class-level
    /// <c>[Authorize(Roles = "Owner")]</c> gate on AuthAdminController
    /// was retired).</param>
    public string Build(
        string tenantSlug,
        Guid userId,
        IReadOnlyCollection<string> includeKeys,
        string? email = null,
        string role = "Owner"
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(includeKeys);

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(_accessTokenTtl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email ?? $"test-{userId:N}@shopflow.local"),
            new("role", role),
            new("tenant_slug", tenantSlug),
        };

        // Sprint-9 KTD1 — one Claim("perm", value) per granted key.
        // JsonWebTokenHandler array-flattens these into a JSON
        // string[] under "perm" at serialization. Policy engine's
        // RequireClaim("perm", key) matches one element at a time.
        foreach (var key in includeKeys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                claims.Add(new Claim("perm", key));
            }
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256),
        };

        return _handler.CreateToken(descriptor);
    }
}
