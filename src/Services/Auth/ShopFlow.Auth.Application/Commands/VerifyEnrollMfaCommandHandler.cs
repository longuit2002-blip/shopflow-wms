using System.Security.Cryptography;
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
/// Sprint-9 U8 — finalise TOTP enrollment by verifying the first OTP.
/// On success: consume Redis enrollment secret + encrypt + persist +
/// generate 10 recovery codes + mark user enrolled + emit MfaEnrolledV1
/// + return token pair + recovery codes ONCE.
/// </summary>
public sealed class VerifyEnrollMfaCommandHandler
    : IRequestHandler<VerifyEnrollMfaCommand, Result<VerifyEnrollMfaResponse>>
{
    private const string InvalidCredentials = "auth.invalid_credentials";
    private const int RecoveryCodeCount = 10;
    private const int RecoveryCodeLength = 10;
    private static readonly char[] RecoveryAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray(); // no 0/O/1/I/L

    private readonly IUserRepository _users;
    private readonly IMfaChallengeTokenCodec _codec;
    private readonly IEnrollmentSecretStore _enrollmentStore;
    private readonly ITotpProvider _totp;
    private readonly ITotpSecretCipher _cipher;
    private readonly ITotpSecretRepository _secrets;
    private readonly IRecoveryCodeRepository _recoveryCodes;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _issuer;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IAuthOutbox _outbox;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<VerifyEnrollMfaCommandHandler> _logger;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;

    public VerifyEnrollMfaCommandHandler(
        IUserRepository users,
        IMfaChallengeTokenCodec codec,
        IEnrollmentSecretStore enrollmentStore,
        ITotpProvider totp,
        ITotpSecretCipher cipher,
        ITotpSecretRepository secrets,
        IRecoveryCodeRepository recoveryCodes,
        IPasswordHasher hasher,
        ITokenIssuer issuer,
        IRefreshTokenStore refreshStore,
        IAuthOutbox outbox,
        IAuthAuditLogRepository auditLog,
        ILogger<VerifyEnrollMfaCommandHandler> logger,
        IRequestContext requestContext,
        TimeProvider clock
    )
    {
        _users = users;
        _codec = codec;
        _enrollmentStore = enrollmentStore;
        _totp = totp;
        _cipher = cipher;
        _secrets = secrets;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _issuer = issuer;
        _refreshStore = refreshStore;
        _outbox = outbox;
        _auditLog = auditLog;
        _logger = logger;
        _requestContext = requestContext;
        _clock = clock;
    }

    public async Task<Result<VerifyEnrollMfaResponse>> Handle(
        VerifyEnrollMfaCommand request,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow().UtcDateTime;
        var payload = _codec.TryDecode(request.EnrollmentToken, now);
        if (
            payload is null
            || payload.Intent != MfaChallengeIntent.Enrollment
            || payload.UserId != request.UserId
        )
        {
            return Result<VerifyEnrollMfaResponse>.Failure(
                "Invalid credentials.",
                InvalidCredentials
            );
        }

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<VerifyEnrollMfaResponse>.Failure(
                "Invalid credentials.",
                InvalidCredentials
            );
        }

        var secret = await _enrollmentStore
            .ConsumeAsync(payload.TenantSlug, user.Id, request.EnrollmentId, ct)
            .ConfigureAwait(false);
        if (secret is null)
        {
            return Result<VerifyEnrollMfaResponse>.Failure(
                "Invalid credentials.",
                InvalidCredentials
            );
        }

        var verify = _totp.VerifyOtp(secret, request.Otp, _clock);
        if (!verify.IsValid)
        {
            return Result<VerifyEnrollMfaResponse>.Failure(
                "Invalid credentials.",
                InvalidCredentials
            );
        }

        var (cipherBlob, keyId) = _cipher.Encrypt(secret, _requestContext.TenantId, user.Id);
        await _secrets
            .UpsertAsync(user.Id, cipherBlob, keyId, verify.TimeStep, ct)
            .ConfigureAwait(false);

        // 10 fresh recovery codes; hash + persist; plaintexts ship to
        // caller exactly once.
        var plaintexts = Enumerable
            .Range(0, RecoveryCodeCount)
            .Select(_ => GenerateCode())
            .ToList();
        var hashes = plaintexts.Select(c => _hasher.Hash(c, Argon2Profile.RecoveryCode)).ToList();
        await _recoveryCodes.AddBatchAsync(user.Id, hashes, ct).ConfigureAwait(false);

        user.MarkMfaEnrolled();
        user.RecordLogin();
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        await _outbox
            .AppendAsync(
                typeof(MfaEnrolledV1).FullName!,
                new MfaEnrolledV1(
                    _requestContext.TenantId,
                    user.Id,
                    user.Email,
                    now,
                    request.CorrelationId
                ),
                ct
            )
            .ConfigureAwait(false);

        await AuthAuditWriter
            .TryAppendAsync(
                _auditLog,
                _logger,
                AuthAuditEventTypes.MfaEnrolled,
                user.Id,
                request.SourceIp,
                request.UserAgent,
                metadata: null,
                request.CorrelationId,
                ct
            )
            .ConfigureAwait(false);

        var access = await _issuer
            .IssueAccessTokenAsync(user, payload.TenantSlug, ct)
            .ConfigureAwait(false);
        var refresh = await _refreshStore
            .IssueAsync(payload.TenantSlug, user.Id, payload.RememberMe, ct)
            .ConfigureAwait(false);

        return Result<VerifyEnrollMfaResponse>.Success(
            new VerifyEnrollMfaResponse(
                AccessToken: access.Jwt,
                AccessTokenExpiresAt: access.ExpiresAt,
                RefreshToken: refresh,
                RefreshTokenExpiresAt: now.AddDays(payload.RememberMe ? 30 : 7),
                RecoveryCodes: new RecoveryCodeView(plaintexts, plaintexts.Count)
            )
        );
    }

    private static string GenerateCode()
    {
        Span<byte> buf = stackalloc byte[RecoveryCodeLength];
        RandomNumberGenerator.Fill(buf);
        Span<char> chars = stackalloc char[RecoveryCodeLength];
        for (var i = 0; i < RecoveryCodeLength; i++)
        {
            chars[i] = RecoveryAlphabet[buf[i] % RecoveryAlphabet.Length];
        }
        return new string(chars);
    }
}
