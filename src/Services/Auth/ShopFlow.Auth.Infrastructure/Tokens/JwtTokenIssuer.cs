using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.Tokens;

/// <summary>
/// HS256 JWT access-token issuer (Sprint-8 U6 / R7). Implements
/// <see cref="ITokenIssuer"/>. Claim shape + signing key + iss/aud
/// kept in lockstep with the kernel
/// <c>AddShopFlowDefaults</c> validator so issuance + validation are
/// guaranteed to agree (KTD5 — the Sprint-6 stub's hardcoded
/// <c>shopflow-wms</c>/<c>shopflow-modules</c> mismatch was caught
/// during doc-review and fixed by sharing the <c>Auth</c> config
/// section between issuer + validator).
/// </summary>
/// <remarks>
/// <para>Claims emitted (R7):</para>
/// <list type="bullet">
///   <item><c>sub</c> — User aggregate Id (Guid string).</item>
///   <item><c>email</c> — normalized lowercase email from the
///     aggregate.</item>
///   <item><c>role</c> — UserRole.ToString() (Owner / Picker /
///     Dispatcher) — the canonical claim every module already reads
///     for authorization (Sprint-7 SignalR hub filter + every Api
///     controller).</item>
///   <item><c>tenant_slug</c> — resolved tenant slug passed in by
///     the caller (the U7 login handler reads it off the routing
///     middleware's RequestContext binding).</item>
///   <item><c>iss</c>, <c>aud</c>, <c>iat</c>, <c>exp</c> — set via
///     <see cref="SecurityTokenDescriptor"/>.</item>
/// </list>
///
/// <para>Signing key is read from <see cref="JwtIssuerOptions.DevSecret"/>
/// which binds to the SAME <c>Auth:DevSecret</c> config key the
/// kernel validator reads — bumping the secret requires updating one
/// place + a coordinated restart of every module that has tokens in
/// flight (15-min access TTL means the rolling window is short).</para>
/// </remarks>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtIssuerOptions _options;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenIssuer(IOptions<JwtIssuerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.DevSecret);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:DevSecret must be at least 32 bytes (UTF-8) for HS256 signing. "
                + "The kernel JwtBearer validator enforces the same minimum at startup; "
                + "an undersize key here would silently produce tokens the validator rejects.");
        }
        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    public AccessToken IssueAccessToken(User user, string tenantSlug)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenTtlMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", user.Role.ToString()),
                new Claim("tenant_slug", tenantSlug),
            }),
            SigningCredentials = new SigningCredentials(
                _signingKey,
                SecurityAlgorithms.HmacSha256),
        };

        var jwt = _handler.CreateToken(descriptor);
        return new AccessToken(jwt, expiresAt);
    }
}
