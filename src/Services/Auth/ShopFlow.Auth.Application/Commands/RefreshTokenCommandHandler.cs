using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 — refresh-token rotation handler. Maps the four
/// <see cref="RefreshRotateOutcome"/> cases to handler results:
/// <list type="bullet">
///   <item>Issued / GraceReplay → new access token + the rotated
///     refresh token (the grace-window pattern returns the same
///     successor token for concurrent retries).</item>
///   <item>NotFound → <c>auth.invalid_credentials</c> (the safe
///     default; force re-login).</item>
///   <item>ReuseDetected → <c>auth.refresh_reused</c> + the store has
///     already revoked every refresh token for the user (KTD3
///     reuse-detection cascade).</item>
/// </list>
/// </summary>
public sealed class RefreshTokenCommandHandler
    : IRequestHandler<RefreshTokenCommand, Result<RefreshResponse>>
{
    private const string InvalidCredentials = "auth.invalid_credentials";
    private const string RefreshReused = "auth.refresh_reused";

    private readonly IRefreshTokenStore _refreshStore;
    private readonly IUserRepository _users;
    private readonly ITokenIssuer _issuer;

    public RefreshTokenCommandHandler(
        IRefreshTokenStore refreshStore,
        IUserRepository users,
        ITokenIssuer issuer)
    {
        _refreshStore = refreshStore;
        _users = users;
        _issuer = issuer;
    }

    public async Task<Result<RefreshResponse>> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result<RefreshResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        var rotation = await _refreshStore
            .RotateAsync(request.TenantSlug, request.UserId, request.RefreshToken, ct)
            .ConfigureAwait(false);

        switch (rotation.Outcome)
        {
            case RefreshRotateOutcome.ReuseDetected:
                return Result<RefreshResponse>.Failure(
                    "Refresh token reuse detected; all sessions revoked.",
                    RefreshReused);

            case RefreshRotateOutcome.NotFound:
                return Result<RefreshResponse>.Failure(
                    "Invalid credentials.",
                    InvalidCredentials);

            case RefreshRotateOutcome.Issued:
            case RefreshRotateOutcome.GraceReplay:
                if (rotation.NewToken is null)
                {
                    // Shouldn't happen — Issued/GraceReplay both carry
                    // the successor token. Defensive: collapse to
                    // invalid_credentials so the user re-logs in.
                    return Result<RefreshResponse>.Failure(
                        "Invalid credentials.", InvalidCredentials);
                }
                break;

            default:
                return Result<RefreshResponse>.Failure(
                    "Invalid credentials.", InvalidCredentials);
        }

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            // User was deactivated mid-session — revoke any remaining
            // refresh tokens so the next refresh re-resolves to
            // invalid_credentials cleanly.
            await _refreshStore
                .RevokeAllForUserAsync(request.TenantSlug, request.UserId, ct)
                .ConfigureAwait(false);
            return Result<RefreshResponse>.Failure(
                "Invalid credentials.", InvalidCredentials);
        }

        var accessToken = await _issuer
            .IssueAccessTokenAsync(user, request.TenantSlug, ct)
            .ConfigureAwait(false);
        // Refresh TTL bucket carried through by the store; recompute the
        // wire value on the same convention the IssueAsync code uses
        // (7d default, 30d remember-me). The handler doesn't know which
        // bucket the original token was issued in — best-effort default
        // of 7d for the wire response; the actual key TTL in Redis is
        // authoritative.
        var refreshExpiresAt = DateTime.UtcNow.AddDays(7);

        return Result<RefreshResponse>.Success(new RefreshResponse(
            AccessToken: accessToken.Jwt,
            AccessTokenExpiresAt: accessToken.ExpiresAt,
            RefreshToken: rotation.NewToken!,
            RefreshTokenExpiresAt: refreshExpiresAt));
    }
}
