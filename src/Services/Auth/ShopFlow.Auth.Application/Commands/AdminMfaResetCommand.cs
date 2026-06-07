using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R17 — Owner-driven MFA reset for a target user. Handler in
/// U8: reject with 422 <c>auth.mfa_required_invariant_owner</c> when
/// the target is an Owner-role user with <c>mfa_required = true</c>
/// (Owner cannot remove MFA from another Owner). Delete the target's
/// TOTP secret + recovery codes + set <c>mfa_enrolled = false</c> +
/// emit MfaResetByOwnerV1 audit.
/// </summary>
public sealed record AdminMfaResetCommand(
    Guid ActorUserId,
    Guid TargetUserId,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId
) : IRequest<Result>;
