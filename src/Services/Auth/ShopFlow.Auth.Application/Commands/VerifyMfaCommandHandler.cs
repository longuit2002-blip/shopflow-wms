using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — second-factor verify after the password step. Accepts
/// either a 6-digit OTP or an 8-char recovery code; both paths converge
/// on token-pair issuance + R6 silent-failure on any mismatch.
/// </summary>
public sealed class VerifyMfaCommandHandler : IRequestHandler<VerifyMfaCommand, Result<LoginResponse>>
{
    private const string InvalidCredentials = "auth.invalid_credentials";

    private readonly IMfaChallengeTokenCodec _codec;
    private readonly IUserRepository _users;
    private readonly ITotpSecretRepository _secrets;
    private readonly ITotpProvider _totp;
    private readonly ITotpSecretCipher _cipher;
    private readonly IRecoveryCodeRepository _recoveryCodes;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _issuer;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<VerifyMfaCommandHandler> _logger;
    private readonly SharedKernel.Application.IRequestContext _requestContext;
    private readonly TimeProvider _clock;

    public VerifyMfaCommandHandler(
        IMfaChallengeTokenCodec codec,
        IUserRepository users,
        ITotpSecretRepository secrets,
        ITotpProvider totp,
        ITotpSecretCipher cipher,
        IRecoveryCodeRepository recoveryCodes,
        IPasswordHasher hasher,
        ITokenIssuer issuer,
        IRefreshTokenStore refreshStore,
        IAuthAuditLogRepository auditLog,
        ILogger<VerifyMfaCommandHandler> logger,
        SharedKernel.Application.IRequestContext requestContext,
        TimeProvider clock)
    {
        _codec = codec;
        _users = users;
        _secrets = secrets;
        _totp = totp;
        _cipher = cipher;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _issuer = issuer;
        _refreshStore = refreshStore;
        _auditLog = auditLog;
        _logger = logger;
        _requestContext = requestContext;
        _clock = clock;
    }

    public async Task<Result<LoginResponse>> Handle(VerifyMfaCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow().UtcDateTime;
        var payload = _codec.TryDecode(request.ChallengeToken, now);
        if (payload is null || payload.Intent != MfaChallengeIntent.Challenge)
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        var user = await _users.GetByIdAsync(payload.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive || !user.MfaEnrolled)
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        var verified = false;
        if (!string.IsNullOrWhiteSpace(request.Otp))
        {
            var view = await _secrets.GetAsync(user.Id, ct).ConfigureAwait(false);
            if (view is null)
            {
                return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
            }
            byte[]? plain;
            try
            {
                plain = _cipher.Decrypt(view.EncryptedSecret, view.KeyId, _requestContext.TenantId, user.Id);
            }
            catch
            {
                plain = null;
            }
            if (plain is null)
            {
                return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
            }

            var result = _totp.VerifyOtp(plain, request.Otp, _clock);
            if (!result.IsValid)
            {
                return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
            }
            if (view.LastUsedTimeStep == result.TimeStep)
            {
                // Within-window replay.
                return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
            }
            await _secrets.UpdateLastUsedStepAsync(user.Id, result.TimeStep, ct).ConfigureAwait(false);
            verified = true;
        }
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            verified = await _recoveryCodes
                .TryConsumeAsync(user.Id, request.RecoveryCode, _hasher, ct)
                .ConfigureAwait(false);
        }

        if (!verified)
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        user.RecordLogin();
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        var access = await _issuer.IssueAccessTokenAsync(user, payload.TenantSlug, ct).ConfigureAwait(false);
        var refresh = await _refreshStore
            .IssueAsync(payload.TenantSlug, user.Id, payload.RememberMe, ct)
            .ConfigureAwait(false);

        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.MfaUsed,
            user.Id,
            request.SourceIp,
            request.UserAgent,
            metadata: null,
            request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result<LoginResponse>.Success(new LoginResponse(
            AccessToken: access.Jwt,
            AccessTokenExpiresAt: access.ExpiresAt,
            RefreshToken: refresh,
            RefreshTokenExpiresAt: now.AddDays(payload.RememberMe ? 30 : 7),
            Role: user.Role.ToString(),
            Email: user.Email));
    }
}
