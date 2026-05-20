using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R31 — anonymous reset-confirm with the token from the
/// email + the new password. Handler in U8: consume token via
/// predicate-in-UPDATE + revoke all refresh tokens for the user +
/// audit-log emit.
/// </summary>
public sealed record ResetPasswordConfirmCommand(
    string Token,
    string NewPassword,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId) : IRequest<Result>;
