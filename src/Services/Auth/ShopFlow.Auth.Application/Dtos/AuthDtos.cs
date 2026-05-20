namespace ShopFlow.Auth.Application.Dtos;

/// <summary>
/// Wire DTOs for the end-user auth flow (Sprint-8 U7 / Sprint-9 U2).
/// All request fields are required at the schema level; per-field
/// validation lives in the handler (email shape, password length,
/// token format). Wire shape is camelCase via
/// <c>AddShopFlowControllers</c> (Sprint-7.5 U1).
/// </summary>
public sealed record LoginRequest(string Email, string Password, bool RememberMe);

/// <summary>
/// Login response — Sprint-9 union of token-pair / MFA-challenge /
/// MFA-enrollment-required shapes. The frontend reads
/// <see cref="MfaRequired"/> / <see cref="MfaEnrollmentRequired"/> first;
/// when either is true the token fields are null and the corresponding
/// short-TTL challenge/enrollment token is populated. Plain happy-path
/// (no MFA) populates the access-token / refresh-token / role / email
/// fields and leaves the MFA flags false.
/// </summary>
public sealed record LoginResponse(
    string? AccessToken = null,
    DateTime? AccessTokenExpiresAt = null,
    string? RefreshToken = null,
    DateTime? RefreshTokenExpiresAt = null,
    string? Role = null,
    string? Email = null,
    bool MfaRequired = false,
    string? MfaChallengeToken = null,
    DateTime? MfaChallengeExpiresAt = null,
    bool MfaEnrollmentRequired = false,
    string? MfaEnrollmentToken = null,
    DateTime? MfaEnrollmentExpiresAt = null);

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
/// revoke-all-other-sessions on the user.
/// </summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// -------- Sprint-9 password reset --------

/// <summary>
/// Sprint-9 R30 — anonymous request to send a password-reset email.
/// Tenant resolution is subdomain-first; the body's
/// <c>TenantSlug</c> is a fallback for callers that hit the API
/// directly without a tenant subdomain (e.g. test harnesses).
/// </summary>
public sealed record ForgotPasswordRequest(string Email, string? TenantSlug = null);

/// <summary>
/// Sprint-9 R31 — anonymous reset-confirm POST carrying the reset
/// token (from the email) + the new password. The token internally
/// resolves to the user + tenant; no tenant slug on the wire.
/// </summary>
public sealed record ResetPasswordConfirmRequest(string Token, string NewPassword);

// -------- Sprint-9 MFA --------

/// <summary>
/// Response from <c>POST /api/auth/mfa/enroll/begin</c>. Carries the
/// <c>otpauth://</c> URI for QR rendering + the enrollment id the
/// verify call echoes back to bind to the Redis-stored secret.
/// </summary>
public sealed record BeginEnrollMfaResponse(
    Guid EnrollmentId,
    string ProvisioningUri,
    string SecretBase32,
    DateTime ExpiresAt);

/// <summary>
/// MFA verify request — used both for login challenge
/// (<c>POST /api/auth/mfa/verify</c>) and for enroll-verify
/// (<c>POST /api/auth/mfa/enroll/verify</c>). Either <see cref="Otp"/>
/// or <see cref="RecoveryCode"/> is required; the recovery-code path
/// is rejected at enroll-verify (no codes exist yet).
/// </summary>
public sealed record VerifyMfaRequest(
    string ChallengeToken,
    string? Otp = null,
    string? RecoveryCode = null);

/// <summary>
/// Enroll-verify request — carries the 6-digit OTP the user typed
/// from the authenticator app + the enrollment id from the
/// begin-enroll response.
/// </summary>
public sealed record VerifyEnrollMfaRequest(
    string EnrollmentToken,
    Guid EnrollmentId,
    string Otp);

/// <summary>
/// Returned ONCE at enrollment-verify success + on
/// regenerate-recovery-codes. Each <c>Code</c> is a plaintext 10-char
/// alphanumeric (~52-bit entropy). The frontend's RecoveryCodesDisplay
/// shows + offers copy/download + requires acknowledge before
/// navigation continues.
/// </summary>
public sealed record RecoveryCodeView(IReadOnlyList<string> Codes, int Count);

/// <summary>Self-service MFA disable — user re-verifies password.</summary>
public sealed record DisableMfaRequest(string CurrentPassword);
