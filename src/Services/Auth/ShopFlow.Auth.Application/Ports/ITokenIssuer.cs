using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the JWT access-token issuer (Sprint-8 U6 ships the
/// HS256-signed impl). The Application layer hands the issuer a
/// <see cref="User"/> + the resolved tenant slug; the impl reads
/// signing-key + iss/aud + lifetime from <c>AuthOptions</c> config
/// and emits a JWT carrying the claims the modules already validate
/// (KTD5 — iss/aud default to <c>shopflow-dev</c>/<c>shopflow-api</c>
/// matching the existing kernel-lifted JwtBearer validator from
/// Sprint-7 U5).
/// </summary>
/// <remarks>
/// <para>Refresh tokens are NOT issued here — they're opaque opaque
/// 32-byte values managed by <see cref="IRefreshTokenStore"/>. JWTs in
/// ShopFlow are short-lived access tokens (R11 — 15-min lifetime);
/// long-lived state lives in Redis where revocation is cheap.</para>
///
/// <para>The issuer is the only place plaintext signing keys live in
/// memory; Sprint-9+ swaps in <c>AddDataProtection().PersistKeysToKeyVault</c>
/// or KMS-backed key rotation when production-grade key management is
/// in scope.</para>
/// </remarks>
public interface ITokenIssuer
{
    /// <summary>
    /// Issue a JWT access token for the user with the resolved tenant
    /// context. Claims included:
    /// <list type="bullet">
    ///   <item><c>sub</c> — user id (Guid string).</item>
    ///   <item><c>email</c> — normalized lowercase email.</item>
    ///   <item><c>role</c> — Owner / Picker / Dispatcher.</item>
    ///   <item><c>tenant_slug</c> — the routing-resolved slug
    ///     (Sprint-7 hub filter + every module's controllers read this).</item>
    ///   <item><c>iss</c> + <c>aud</c> — bound to
    ///     <c>AuthOptions.Issuer</c>/<c>Audience</c> so the kernel JWT
    ///     validator from Sprint-7 U5 accepts the token (KTD5).</item>
    ///   <item><c>exp</c> — now + <c>AuthOptions.AccessTokenLifetimeMinutes</c>
    ///     (default 15).</item>
    /// </list>
    /// </summary>
    AccessToken IssueAccessToken(User user, string tenantSlug);
}

/// <summary>
/// Wire envelope for the issued access token. <see cref="ExpiresAt"/>
/// is in UTC and lets the LoginResponse / RefreshResponse DTO carry
/// the absolute expiry the frontend uses to schedule the next
/// refresh (R11 + R13).
/// </summary>
public sealed record AccessToken(string Jwt, DateTime ExpiresAt);
