using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R21 — Owner-driven manual unlock for a locked user.
/// Handler in U8: clear <c>locked_until</c> + reset
/// <c>failed_login_count</c> + emit AccountUnlockedByOwnerV1 audit.
/// </summary>
public sealed record AdminUnlockAccountCommand(
    Guid ActorUserId,
    Guid TargetUserId,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId
) : IRequest<Result>;
