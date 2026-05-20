using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the JWT access-token issuer (Sprint-8 U6 ships the
/// HS256-signed impl; Sprint-9 U6 bumps the signature to async + reads
/// the user's permission list from <see cref="IRolePermissionRepository"/>
/// to project the <c>perm</c> claim per KTD1). The Application layer
/// hands the issuer a <see cref="User"/> + the resolved tenant slug; the
/// impl reads signing-key + iss/aud + lifetime from <c>AuthOptions</c>
/// (KTD5 — iss/aud default to <c>shopflow-dev</c>/<c>shopflow-api</c>
/// matching the existing kernel-lifted JwtBearer validator from
/// Sprint-7 U5).
/// </summary>
/// <remarks>
/// <para>Refresh tokens are NOT issued here — they're opaque 32-byte
/// values managed by <see cref="IRefreshTokenStore"/>. JWTs in
/// ShopFlow are short-lived access tokens (R11 — 15-min lifetime);
/// long-lived state lives in Redis where revocation is cheap.</para>
///
/// <para>The issuer is the only place plaintext signing keys live in
/// memory; Sprint-10+ swaps in <c>AddDataProtection().PersistKeysToKeyVault</c>
/// or KMS-backed key rotation when production-grade key management is
/// in scope.</para>
///
/// <para>Sprint-9 U2 keeps the sync <see cref="IssueAccessToken"/>
/// shape to avoid bleeding handler-update work into U2; U6 replaces it
/// with an async overload reading
/// <see cref="IRolePermissionRepository"/> and updates the three
/// Sprint-8 handlers (Login / Refresh / ChangePassword) at the same
/// time.</para>
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
    ///   <item><c>tenant_slug</c> — the routing-resolved slug.</item>
    ///   <item><c>perm</c> — JSON string array of permission keys (U6
    ///     adds this claim once the async overload lands).</item>
    ///   <item><c>iss</c> + <c>aud</c> — bound to
    ///     <c>AuthOptions.Issuer</c>/<c>Audience</c>.</item>
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
