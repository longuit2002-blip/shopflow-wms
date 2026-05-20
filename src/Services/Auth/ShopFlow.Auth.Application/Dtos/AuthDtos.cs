namespace ShopFlow.Auth.Application.Dtos;

/// <summary>
/// Wire DTOs for the end-user auth flow (Sprint-8 U7 ships the handler
/// surfaces). All request fields are required at the schema level;
/// per-field validation lives in the handler (email shape, password
/// length, token format). Wire shape is camelCase via
/// <c>AddShopFlowControllers</c> (Sprint-7.5 U1).
/// </summary>
public sealed record LoginRequest(string Email, string Password, bool RememberMe);

/// <summary>
/// Login success response. Carries both tokens + the absolute access
/// expiry timestamp + the user's role so the frontend can render the
/// correct nav-rail without a follow-up profile call (R11 + R13). The
/// <c>refreshExpiresAt</c> is informational — the refresh token itself
/// is opaque and the frontend reads its TTL from this field for the
/// "session-expired" banner trigger (R13).
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    string Role,
    string Email);

public sealed record RefreshRequest(string RefreshToken);

public sealed record RefreshResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

/// <summary>
/// Logout payload — single-token revocation. <c>AllDevices=true</c>
/// triggers the user-wide revoke-all path (R14). The access-token
/// claims supply the tenant + user; the body only needs the refresh
/// token (and the optional all-devices flag).
/// </summary>
public sealed record LogoutRequest(string RefreshToken, bool AllDevices);

/// <summary>
/// Self-service password change (R15). Caller is authenticated via
/// the access token; current password gates the change to prevent
/// session-hijack-then-change. Successful change triggers
/// revoke-all-other-sessions on the user (R10 — fresh password,
/// fresh session).
/// </summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
