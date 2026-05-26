using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — self-service MFA disable. Requires current password
/// re-verify; rejected when the user has <c>mfa_required = true</c>
/// (Owner invariant + R17). Sprint-12.5 U1 — emits
/// <c>auth.mfa.disabled</c> only on the successful disable path
/// (per R3, audit captures successful actions; the
/// <c>auth.mfa_required_cannot_disable</c> rejection has no audit row).
/// </summary>
public sealed class DisableMfaCommandHandler : IRequestHandler<DisableMfaCommand, Result>
{
    private const string InvalidCredentials = "auth.invalid_credentials";
    private const string CannotDisable = "auth.mfa_required_cannot_disable";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITotpSecretRepository _secrets;
    private readonly IRecoveryCodeRepository _recoveryCodes;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<DisableMfaCommandHandler> _logger;

    public DisableMfaCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        ITotpSecretRepository secrets,
        IRecoveryCodeRepository recoveryCodes,
        IAuthAuditLogRepository auditLog,
        ILogger<DisableMfaCommandHandler> logger)
    {
        _users = users;
        _hasher = hasher;
        _secrets = secrets;
        _recoveryCodes = recoveryCodes;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result> Handle(DisableMfaCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        if (user.MfaRequired)
        {
            return Result.Failure(
                "MFA is required for this user and cannot be disabled.",
                CannotDisable);
        }

        await _secrets.DeleteAsync(user.Id, ct).ConfigureAwait(false);
        await _recoveryCodes.DeleteAllAsync(user.Id, ct).ConfigureAwait(false);
        user.MarkMfaDisabled();
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.MfaDisabled,
            user.Id,
            request.SourceIp,
            request.UserAgent,
            metadata: null,
            request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result.Success();
    }
}
