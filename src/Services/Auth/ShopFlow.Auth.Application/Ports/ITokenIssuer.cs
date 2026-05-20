using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the JWT access-token issuer (Sprint-8 U6 shipped the
/// initial sync HS256 impl; Sprint-9 U6 bumps to async + reads the
/// user's role permissions for the <c>perm</c> claim projection per
/// KTD1). The Application layer hands the issuer a <see cref="User"/>
/// + the resolved tenant slug; the impl reads signing-key + iss/aud +
/// lifetime from <c>AuthOptions</c> (KTD5 — iss/aud match the kernel
/// JwtBearer validator).
/// </summary>
/// <remarks>
/// <para>Refresh tokens are NOT issued here — they're opaque 32-byte
/// values managed by <see cref="IRefreshTokenStore"/>. JWTs in
/// ShopFlow are short-lived access tokens (R11 — 15-min lifetime);
/// long-lived state lives in Redis where revocation is cheap.</para>
///
/// <para>The <c>perm</c> claim is emitted as a JSON string array via
/// multiple <c>Claim("perm", value)</c> entries — <c>JsonWebTokenHandler</c>
/// flattens claims with identical types into a single JSON array
/// under the claim name. <c>RequireClaim("perm", "&lt;key&gt;")</c>
/// in U7 policy registration matches one element at a time. Do NOT
/// space-delimit the perms into one claim — that would break the
/// policy matchers.</para>
/// </remarks>
public interface ITokenIssuer
{
    /// <summary>
    /// Issue a JWT access token. Claims: <c>sub</c>, <c>email</c>,
    /// <c>role</c>, <c>tenant_slug</c>, <c>perm</c> (JSON array),
    /// <c>iss</c>, <c>aud</c>, <c>iat</c>, <c>exp</c>.
    /// </summary>
    Task<AccessToken> IssueAccessTokenAsync(User user, string tenantSlug, CancellationToken ct);
}

/// <summary>
/// Wire envelope for the issued access token.
/// </summary>
public sealed record AccessToken(string Jwt, DateTime ExpiresAt);
