using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R12 — finalise TOTP enrollment by verifying the first OTP.
/// Handler in U8: consume Redis enrollment secret + verify OTP +
/// encrypt secret via <c>ITotpSecretCipher</c> + persist + generate 10
/// recovery codes + return RecoveryCodeView ONCE + emit MfaEnrolledV1.
/// On success the response also carries the full token-pair so the
/// user's session activates without an additional login round-trip.
/// </summary>
public sealed record VerifyEnrollMfaCommand(
    Guid UserId,
    string TenantSlug,
    string EnrollmentToken,
    Guid EnrollmentId,
    string Otp,
    bool RememberMe,
    Guid CorrelationId) : IRequest<Result<VerifyEnrollMfaResponse>>;

/// <summary>
/// Result of <see cref="VerifyEnrollMfaCommand"/>. Carries the active
/// token pair (the user is now signed in) + the recovery codes shown
/// ONCE to the user (RecoveryCodesDisplay frontend acknowledgement
/// gate is mandatory before navigation continues).
/// </summary>
public sealed record VerifyEnrollMfaResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    RecoveryCodeView RecoveryCodes);
