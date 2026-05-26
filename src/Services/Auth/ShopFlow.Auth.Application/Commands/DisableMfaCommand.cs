using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R16 — self-service MFA disable. Handler in U8: require
/// password re-verify + reject with 422 <c>auth.mfa_required_cannot_disable</c>
/// when <c>mfa_required = true</c> for the user + delete secret +
/// delete recovery codes + emit MfaDisabledV1 audit.
/// </summary>
public sealed record DisableMfaCommand(
    Guid UserId,
    string TenantSlug,
    string CurrentPassword,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId
) : IRequest<Result>;
