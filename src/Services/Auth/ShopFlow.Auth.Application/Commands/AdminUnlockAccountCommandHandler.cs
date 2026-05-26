using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — Owner-driven manual unlock for a locked user. Clears
/// <c>locked_until</c> + <c>failed_login_count</c> + <c>last_failed_login_at</c>.
/// Sprint-12.5 U1 — emits <c>auth.account.unlocked_by_owner</c> on the
/// successful unlock path with <c>targetUserId</c> in metadata.
/// </summary>
public sealed class AdminUnlockAccountCommandHandler : IRequestHandler<AdminUnlockAccountCommand, Result>
{
    private const string TargetNotFound = "auth.target_not_found";

    private readonly IUserRepository _users;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<AdminUnlockAccountCommandHandler> _logger;

    public AdminUnlockAccountCommandHandler(
        IUserRepository users,
        IAuthAuditLogRepository auditLog,
        ILogger<AdminUnlockAccountCommandHandler> logger)
    {
        _users = users;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result> Handle(AdminUnlockAccountCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await _users.GetByIdAsync(request.TargetUserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result.Failure("Target user not found.", TargetNotFound);
        }

        target.Unlock();
        await _users.UpdateAsync(target, ct).ConfigureAwait(false);

        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.AccountUnlockedByOwner,
            request.ActorUserId,
            request.SourceIp,
            request.UserAgent,
            new { targetUserId = request.TargetUserId.ToString() },
            request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result.Success();
    }
}
