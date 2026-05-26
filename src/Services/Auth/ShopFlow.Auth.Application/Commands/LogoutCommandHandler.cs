using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 — logout handler. Idempotent: revoking a missing /
/// already-revoked token returns success rather than 404, so the
/// frontend doesn't have to special-case the "logged out from
/// another tab" race (R6 + R14). Sprint-12.5 U1 — emits
/// <c>auth.logout</c> on every successful (or no-op success) path.
/// </summary>
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenStore refreshStore,
        IAuthAuditLogRepository auditLog,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshStore = refreshStore;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            // Defensive — controller validation should catch this, but
            // logout treats an empty token as a no-op success. No audit
            // row — there's no logout action to record.
            return Result.Success();
        }

        if (request.AllDevices)
        {
            await _refreshStore
                .RevokeAllForUserAsync(request.TenantSlug, request.UserId, ct)
                .ConfigureAwait(false);
        }
        else
        {
            await _refreshStore
                .RevokeAsync(request.TenantSlug, request.UserId, request.RefreshToken, ct)
                .ConfigureAwait(false);
        }

        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.Logout,
            request.UserId,
            request.SourceIp,
            request.UserAgent,
            metadata: null,
            request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result.Success();
    }
}
