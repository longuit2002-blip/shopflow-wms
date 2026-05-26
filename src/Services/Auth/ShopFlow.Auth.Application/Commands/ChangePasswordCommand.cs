using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// MediatR command for self-service password change (Sprint-8 U7 /
/// R15 / F7). The access-token claims supply <see cref="UserId"/> +
/// <see cref="TenantSlug"/>; <see cref="CurrentPassword"/> gates the
/// rotation to prevent session-hijack-then-change.
/// </summary>
public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword,
    Guid UserId,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId) : IRequest<Result>;
