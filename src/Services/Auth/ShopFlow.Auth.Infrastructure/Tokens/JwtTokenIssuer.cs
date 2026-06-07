using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.Tokens;

/// <summary>
/// HS256 JWT access-token issuer (Sprint-8 U6 / Sprint-9 U6).
/// Implements <see cref="ITokenIssuer"/>. Claim shape + signing key +
/// iss/aud stay in lockstep with the kernel
/// <c>AddShopFlowDefaults</c> validator (KTD5).
/// </summary>
/// <remarks>
/// <para>Sprint-9 grafts the <c>perm</c> JSON-array claim per KTD1.
/// The issuer reads <see cref="IRolePermissionRepository"/> for the
/// user's role at issuance time and emits one
/// <c>Claim("perm", &lt;key&gt;)</c> per granted permission.
/// <c>JsonWebTokenHandler</c> flattens identical-type claims into a
/// JSON array under the claim name; the U7 policy registration uses
/// <c>RequireClaim("perm", &lt;key&gt;)</c> which matches one element
/// at a time. Do NOT collapse into a single space-delimited string —
/// the policy matcher does exact-value equality and would never
/// match a multi-perm string.</para>
///
/// <para>Claims emitted (R7 + Sprint-9 R5):</para>
/// <list type="bullet">
///   <item><c>sub</c> — User Id.</item>
///   <item><c>email</c> — normalized lowercase email.</item>
///   <item><c>role</c> — UserRole.ToString().</item>
///   <item><c>tenant_slug</c> — resolved slug.</item>
///   <item><c>perm</c> (×N) — one per granted permission.</item>
///   <item><c>iss</c>, <c>aud</c>, <c>iat</c>, <c>exp</c>.</item>
/// </list>
/// </remarks>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtIssuerOptions _options;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenIssuer(
        IOptions<JwtIssuerOptions> options,
        IRolePermissionRepository rolePermissions
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rolePermissions);
        _options = options.Value;
        _rolePermissions = rolePermissions;

        var keyBytes = Encoding.UTF8.GetBytes(_options.DevSecret);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:DevSecret must be at least 32 bytes (UTF-8) for HS256 signing. "
                    + "The kernel JwtBearer validator enforces the same minimum at startup; "
                    + "an undersize key here would silently produce tokens the validator rejects."
            );
        }
        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    public async Task<AccessToken> IssueAccessTokenAsync(
        User user,
        string tenantSlug,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenTtlMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("role", user.Role.ToString()),
            new("tenant_slug", tenantSlug),
        };

        // Sprint-9 KTD1 — one Claim("perm", value) per granted key.
        // The JsonWebTokenHandler serializes multiple same-type claims
        // into a JSON array under "perm".
        var permissions = await _rolePermissions
            .GetForRoleAsync(user.Role, ct)
            .ConfigureAwait(false);
        foreach (var perm in permissions)
        {
            claims.Add(new Claim("perm", perm));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256),
        };

        var jwt = _handler.CreateToken(descriptor);
        return new AccessToken(jwt, expiresAt);
    }
}
