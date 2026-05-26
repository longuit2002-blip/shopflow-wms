using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Contracts.Auth;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 — refresh-token rotation handler. Sprint-12.5 U1 — wires
/// <c>auth.refresh.success</c> on Issued/GraceReplay + <c>auth.refresh.reused</c>
/// on ChainRevoked/ReuseDetected. Maps the four
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
    private readonly IAuthOutbox _outbox;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;
    private readonly IRequestContext _requestContext;

    public RefreshTokenCommandHandler(
        IRefreshTokenStore refreshStore,
        IUserRepository users,
        ITokenIssuer issuer,
        IAuthOutbox outbox,
        IAuthAuditLogRepository auditLog,
        ILogger<RefreshTokenCommandHandler> logger,
        IRequestContext requestContext)
    {
        _refreshStore = refreshStore;
        _users = users;
        _issuer = issuer;
        _outbox = outbox;
        _auditLog = auditLog;
        _logger = logger;
        _requestContext = requestContext;
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
            case RefreshRotateOutcome.ChainRevoked:
            case RefreshRotateOutcome.ReuseDetected:
                // Sprint-9 — emit cross-module event so Notification fans
                // out to Owner role users (R28). Both Sprint-9
                // ChainRevoked + Sprint-8-legacy ReuseDetected paths
                // surface the same response shape; the store has already
                // revoked (chain-only or user-wide depending on outcome).
                var presentedHash = HashHex(request.RefreshToken);
                var revokedAt = DateTime.UtcNow;
                await _outbox.AppendAsync(
                    typeof(RefreshReuseDetectedV1).FullName!,
                    new RefreshReuseDetectedV1(
                        TenantId: _requestContext.TenantId,
                        UserId: request.UserId,
                        AffectedUserEmail: string.Empty, // resolved by Notification consumer if needed
                        ChainId: rotation.ChainId ?? Guid.Empty,
                        PresentedTokenHash: presentedHash,
                        PresentingIp: "unknown",
                        UserAgent: "unknown",
                        OccurredAtUtc: revokedAt,
                        CorrelationId: Guid.NewGuid()),
                    ct).ConfigureAwait(false);

                await AuthAuditWriter.TryAppendAsync(
                    _auditLog,
                    _logger,
                    AuthAuditEventTypes.RefreshReused,
                    request.UserId,
                    request.SourceIp,
                    request.UserAgent,
                    new
                    {
                        chainId = (rotation.ChainId ?? Guid.Empty).ToString(),
                        revokedAt = revokedAt.ToString("O"),
                    },
                    request.CorrelationId,
                    ct).ConfigureAwait(false);

                return Result<RefreshResponse>.Failure(
                    "Refresh token reuse detected.",
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

        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.RefreshSuccess,
            request.UserId,
            request.SourceIp,
            request.UserAgent,
            new { chainId = (rotation.ChainId ?? Guid.Empty).ToString() },
            request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result<RefreshResponse>.Success(new RefreshResponse(
            AccessToken: accessToken.Jwt,
            AccessTokenExpiresAt: accessToken.ExpiresAt,
            RefreshToken: rotation.NewToken!,
            RefreshTokenExpiresAt: refreshExpiresAt));
    }

    private static string HashHex(string plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(plaintext), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
