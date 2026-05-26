using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — Owner-driven MFA reset for a target user. Per R17:
/// an Owner cannot disable MFA on another Owner — rejected with 422
/// <c>auth.mfa_required_invariant_owner</c>. Sprint-12.5 U1 — emits
/// <c>auth.mfa.reset_by_owner</c> on the successful reset path, with
/// <c>targetUserId</c> in metadata so the row identifies both the
/// actor (audit row's userId) and the subject of the reset.
/// </summary>
public sealed class AdminMfaResetCommandHandler : IRequestHandler<AdminMfaResetCommand, Result>
{
    private const string InvariantOwner = "auth.mfa_required_invariant_owner";
    private const string TargetNotFound = "auth.target_not_found";

    private readonly IUserRepository _users;
    private readonly ITotpSecretRepository _secrets;
    private readonly IRecoveryCodeRepository _recoveryCodes;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<AdminMfaResetCommandHandler> _logger;

    public AdminMfaResetCommandHandler(
        IUserRepository users,
        ITotpSecretRepository secrets,
        IRecoveryCodeRepository recoveryCodes,
        IAuthAuditLogRepository auditLog,
        ILogger<AdminMfaResetCommandHandler> logger
    )
    {
        _users = users;
        _secrets = secrets;
        _recoveryCodes = recoveryCodes;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result> Handle(AdminMfaResetCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await _users.GetByIdAsync(request.TargetUserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result.Failure("Target user not found.", TargetNotFound);
        }

        if (target.Role == UserRole.Owner && target.MfaRequired)
        {
            return Result.Failure(
                "Cannot reset MFA on an Owner role user — Owner MFA is invariant.",
                InvariantOwner
            );
        }

        await _secrets.DeleteAsync(target.Id, ct).ConfigureAwait(false);
        await _recoveryCodes.DeleteAllAsync(target.Id, ct).ConfigureAwait(false);
        target.MarkMfaReset();
        await _users.UpdateAsync(target, ct).ConfigureAwait(false);

        // Audit row's userId is the ACTOR (Owner performing the reset),
        // metadata.targetUserId identifies the subject. Mirrors
        // RolePermissionsChanged's actor-vs-target separation.
        await AuthAuditWriter
            .TryAppendAsync(
                _auditLog,
                _logger,
                AuthAuditEventTypes.MfaResetByOwner,
                request.ActorUserId,
                request.SourceIp,
                request.UserAgent,
                new { targetUserId = request.TargetUserId.ToString() },
                request.CorrelationId,
                ct
            )
            .ConfigureAwait(false);

        return Result.Success();
    }
}
