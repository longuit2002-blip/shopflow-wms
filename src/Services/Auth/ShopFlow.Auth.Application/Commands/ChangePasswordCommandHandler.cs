using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 — self-service password change handler. After a
/// successful rotation, revokes EVERY refresh token for the user
/// (R10 + R15 — fresh password means fresh session everywhere). The
/// caller's current refresh token also goes; the frontend re-logs in
/// after a 200 response. Sprint-12.5 U1 — emits
/// <c>auth.password.changed</c> on the successful rotation path.
/// </summary>
/// <remarks>
/// <para>Minimum password length is 8 chars per R17 baseline. Equality
/// with the current password is allowed (we don't enforce
/// password-history) — re-hashing under a new salt is still
/// meaningful work if a tenant insists; <c>auth.password_unchanged</c>
/// from the plan's defensive case is intentionally NOT implemented to
/// keep handler complexity bounded.</para>
/// </remarks>
public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result>
{
    private const string InvalidCredentials = "auth.invalid_credentials";
    private const string PasswordTooShort = "auth.password_too_short";
    private const int MinPasswordLength = 8;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IRefreshTokenStore refreshStore,
        IAuthAuditLogRepository auditLog,
        ILogger<ChangePasswordCommandHandler> logger
    )
    {
        _users = users;
        _hasher = hasher;
        _refreshStore = refreshStore;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (
            string.IsNullOrWhiteSpace(request.NewPassword)
            || request.NewPassword.Length < MinPasswordLength
        )
        {
            return Result.Failure(
                $"New password must be at least {MinPasswordLength} characters.",
                PasswordTooShort
            );
        }

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        var newHash = _hasher.Hash(request.NewPassword);
        user.UpdatePassword(newHash);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        // Revoke every refresh session — the caller logs in fresh.
        await _refreshStore
            .RevokeAllForUserAsync(request.TenantSlug, request.UserId, ct)
            .ConfigureAwait(false);

        await AuthAuditWriter
            .TryAppendAsync(
                _auditLog,
                _logger,
                AuthAuditEventTypes.PasswordChanged,
                request.UserId,
                request.SourceIp,
                request.UserAgent,
                metadata: null,
                request.CorrelationId,
                ct
            )
            .ConfigureAwait(false);

        return Result.Success();
    }
}
