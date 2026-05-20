using MediatR;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 — logout handler. Idempotent: revoking a missing /
/// already-revoked token returns success rather than 404, so the
/// frontend doesn't have to special-case the "logged out from
/// another tab" race (R6 + R14).
/// </summary>
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenStore _refreshStore;

    public LogoutCommandHandler(IRefreshTokenStore refreshStore)
    {
        _refreshStore = refreshStore;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            // Defensive — controller validation should catch this, but
            // logout treats an empty token as a no-op success.
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

        return Result.Success();
    }
}
