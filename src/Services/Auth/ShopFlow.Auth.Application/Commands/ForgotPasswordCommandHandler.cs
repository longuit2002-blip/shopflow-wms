using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Contracts.Auth;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — forgot-password initiator. R6 + R32 + R29: always
/// returns success (caller sees generic confirmation) regardless of
/// outcome; per-account cooldown silently skips emit on repeat
/// requests; synthetic Argon2 verify keeps wall-time constant on
/// unknown email (KTD14).
/// </summary>
public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAuthOutbox _outbox;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly AuthPasswordResetOptions _options;

    public ForgotPasswordCommandHandler(
        IUserRepository users,
        IPasswordResetTokenRepository resetTokens,
        IPasswordHasher hasher,
        IAuthOutbox outbox,
        IAuthAuditLogRepository auditLog,
        ILogger<ForgotPasswordCommandHandler> logger,
        IRequestContext requestContext,
        TimeProvider clock,
        IOptions<AuthPasswordResetOptions> options)
    {
        _users = users;
        _resetTokens = resetTokens;
        _hasher = hasher;
        _outbox = outbox;
        _auditLog = auditLog;
        _logger = logger;
        _requestContext = requestContext;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await _users.GetByEmailAsync(request.Email, ct).ConfigureAwait(false);

        // KTD14 — constant-time response. Run a dummy Argon2 verify
        // against the configured sentinel so unknown-email wall-time
        // matches the known-email path.
        if (user is null || !user.IsActive)
        {
            _ = _hasher.Verify(request.Email ?? string.Empty, _options.SyntheticHash);
            return Result.Success();
        }

        var lastIssuedAt = await _resetTokens.GetLastIssuedAtAsync(user.Id, ct).ConfigureAwait(false);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (lastIssuedAt is not null
            && now - lastIssuedAt.Value < TimeSpan.FromMinutes(_options.CooldownMinutes))
        {
            // R32 — silent skip on cooldown active.
            _ = _hasher.Verify(request.Email!, _options.SyntheticHash);
            return Result.Success();
        }

        // Generate fresh 32-byte CSPRNG token. The plaintext lives in
        // local scope only; we persist SHA-256(plaintext) + emit the
        // composed URL to the outbox, then plaintext goes out of scope.
        var rawToken = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToBase64String(rawToken)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        var expiresAt = now.AddMinutes(_options.TokenTtlMinutes);

        var addResult = await _resetTokens.AddAsync(tokenHash, user.Id, expiresAt, ct).ConfigureAwait(false);
        if (!addResult.IsSuccess)
        {
            // Astronomically unlikely 23505 — silent success per R6.
            return Result.Success();
        }

        var resetUrl = _options.WorkspaceUrlTemplate
            .Replace("{slug}", request.TenantSlug)
            + $"/reset-password?token={plaintext}";

        await _outbox.AppendAsync(
            typeof(PasswordResetRequestedV1).FullName!,
            new PasswordResetRequestedV1(
                _requestContext.TenantId,
                user.Id,
                user.Email,
                request.TenantSlug,
                resetUrl,
                expiresAt,
                now,
                request.CorrelationId),
            ct).ConfigureAwait(false);

        // Sprint-12.5 U1 — audit only on the path that actually emitted
        // a reset token. The R6 silent-skip / unknown-email paths above
        // return success without ever issuing a token, so they do NOT
        // emit auth.password.reset.requested (audit captures real actions).
        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.PasswordResetRequested,
            user.Id,
            request.SourceIp,
            request.UserAgent,
            metadata: null,
            request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result.Success();
    }
}
