using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R13/R14 — verify the OTP or recovery code that completes
/// the MFA challenge after a successful password verify. Handler in
/// U8: decode challenge token (carries user_id + tenant_slug +
/// remember_me + expiry) + verify OTP (with last_used_step replay
/// guard) OR consume recovery code (predicate-in-UPDATE single-use) +
/// emit MfaUsedV1 audit + return token pair.
/// </summary>
/// <param name="Otp">6-digit TOTP code; mutually exclusive with <paramref name="RecoveryCode"/>.</param>
/// <param name="RecoveryCode">8-char alphanumeric recovery code; mutually exclusive with <paramref name="Otp"/>.</param>
public sealed record VerifyMfaCommand(
    string ChallengeToken,
    string? Otp,
    string? RecoveryCode,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId
) : IRequest<Result<LoginResponse>>;
