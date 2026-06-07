using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Contracts.Auth;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 / Sprint-9 U8 — login handler. Extended with per-account
/// lockout (R18-R22) + MFA branch (R10-R14) + AccountLockedV1 emission
/// on the lockout boundary. Sprint-12.5 U1 — wires
/// <c>auth.login.success</c> / <c>auth.login.failed</c> /
/// <c>auth.login.locked</c> audit rows at every terminal emit.
/// </summary>
/// <remarks>
/// <para>Every credential failure leg still collapses to
/// <c>auth.invalid_credentials</c> per R6: silent-locked = same shape
/// as wrong-password = same shape as missing-user. The handler emits
/// <c>AccountLockedV1</c> exactly once on the boundary attempt; calls
/// to a still-locked user do not re-emit. The audit row follows the
/// same semantic — <c>auth.login.locked</c> fires once at the lockout
/// boundary attempt (NOT on subsequent silent-locked retries) per
/// Sprint-12.5 U1 KTD design.</para>
///
/// <para>MFA branch returns 200 with <c>MfaRequired = true</c> +
/// <c>MfaChallengeToken</c> when the user has MFA enrolled, or
/// <c>MfaEnrollmentRequired = true</c> + <c>MfaEnrollmentToken</c>
/// when the user is required to enroll (Owner role per
/// <c>User.MfaRequired</c>). No access/refresh token is issued until
/// the verify step.</para>
/// </remarks>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private const string InvalidCredentials = "auth.invalid_credentials";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _issuer;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly IMfaChallengeTokenCodec _mfaCodec;
    private readonly IAuthOutbox _outbox;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<LoginCommandHandler> _logger;
    private readonly TimeProvider _clock;
    private readonly AuthLockoutOptions _lockout;
    private readonly IRequestContext _requestContext;

    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        ITokenIssuer issuer,
        IRefreshTokenStore refreshStore,
        IMfaChallengeTokenCodec mfaCodec,
        IAuthOutbox outbox,
        IAuthAuditLogRepository auditLog,
        ILogger<LoginCommandHandler> logger,
        TimeProvider clock,
        IOptions<AuthLockoutOptions> lockout,
        IRequestContext requestContext
    )
    {
        _users = users;
        _hasher = hasher;
        _issuer = issuer;
        _refreshStore = refreshStore;
        _mfaCodec = mfaCodec;
        _outbox = outbox;
        _auditLog = auditLog;
        _logger = logger;
        _clock = clock;
        _lockout = lockout.Value;
        _requestContext = requestContext;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            await EmitLoginFailedAsync(userId: null, request, ct).ConfigureAwait(false);
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        var user = await _users.GetByEmailAsync(request.Email, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            // R6 — same 401 shape as known-user wrong-password. KTD9 —
            // metadata captures submittedEmail for forensic correlation
            // even though the HTTP response stays uniform.
            await EmitLoginFailedAsync(userId: null, request, ct).ConfigureAwait(false);
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        // Sprint-9 — silent-locked check BEFORE password verify. Same
        // 401 shape; no signal to caller that the account is locked.
        // Sprint-12.5 U1 — subsequent already-locked retries do NOT
        // re-emit auth.login.locked; they get auth.login.failed.
        if (user.LockedUntil is not null && user.LockedUntil.Value > now)
        {
            await EmitLoginFailedAsync(user.Id, request, ct).ConfigureAwait(false);
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            var triggeredLockout = user.RegisterFailedLogin(
                _clock,
                _lockout.MaxAttempts,
                TimeSpan.FromMinutes(_lockout.WindowMinutes),
                TimeSpan.FromMinutes(_lockout.DurationMinutes)
            );
            await _users.UpdateAsync(user, ct).ConfigureAwait(false);

            if (triggeredLockout && user.LockedUntil is not null)
            {
                await _outbox
                    .AppendAsync(
                        typeof(AccountLockedV1).FullName!,
                        new AccountLockedV1(
                            _requestContext.TenantId,
                            user.Id,
                            user.Email,
                            user.FailedLoginCount,
                            user.LockedUntil.Value,
                            "unknown", // source IP filled by the controller in U9
                            now,
                            Guid.NewGuid()
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                // Sprint-12.5 U1 — auth.login.locked fires once at the
                // lockout boundary, alongside AccountLockedV1. The
                // wrong-password attempt that crossed the threshold
                // gets BOTH rows (failed + locked) per plan U1 test
                // scenario.
                await AuthAuditWriter
                    .TryAppendAsync(
                        _auditLog,
                        _logger,
                        AuthAuditEventTypes.LoginFailed,
                        user.Id,
                        request.SourceIp,
                        request.UserAgent,
                        new { reason = InvalidCredentials, submittedEmail = request.Email },
                        request.CorrelationId,
                        ct
                    )
                    .ConfigureAwait(false);

                await AuthAuditWriter
                    .TryAppendAsync(
                        _auditLog,
                        _logger,
                        AuthAuditEventTypes.LoginLocked,
                        user.Id,
                        request.SourceIp,
                        request.UserAgent,
                        new { lockedUntil = user.LockedUntil.Value.ToString("O") },
                        request.CorrelationId,
                        ct
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                await EmitLoginFailedAsync(user.Id, request, ct).ConfigureAwait(false);
            }

            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        // Password verified — Sprint-9 MFA branch. MFA-challenge /
        // MFA-enrollment paths do NOT emit auth.login.success — the
        // user hasn't completed the second factor yet. VerifyMfa or
        // VerifyEnrollMfa handlers emit auth.mfa.used / auth.mfa.enrolled
        // on the terminal token issuance.
        if (user.MfaRequired && user.MfaEnrolled)
        {
            var challengeToken = _mfaCodec.Issue(
                user.Id,
                request.TenantSlug,
                request.RememberMe,
                MfaChallengeIntent.Challenge,
                now
            );
            return Result<LoginResponse>.Success(
                new LoginResponse(
                    MfaRequired: true,
                    MfaChallengeToken: challengeToken,
                    MfaChallengeExpiresAt: now.AddMinutes(5)
                )
            );
        }

        if (user.MfaRequired && !user.MfaEnrolled)
        {
            var enrollmentToken = _mfaCodec.Issue(
                user.Id,
                request.TenantSlug,
                request.RememberMe,
                MfaChallengeIntent.Enrollment,
                now
            );
            return Result<LoginResponse>.Success(
                new LoginResponse(
                    MfaEnrollmentRequired: true,
                    MfaEnrollmentToken: enrollmentToken,
                    MfaEnrollmentExpiresAt: now.AddMinutes(5)
                )
            );
        }

        // Happy path — issue access + refresh token pair.
        user.RecordLogin();
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        var accessToken = await _issuer
            .IssueAccessTokenAsync(user, request.TenantSlug, ct)
            .ConfigureAwait(false);
        var refreshToken = await _refreshStore
            .IssueAsync(request.TenantSlug, user.Id, request.RememberMe, ct)
            .ConfigureAwait(false);
        var refreshExpiresAt = now.AddDays(request.RememberMe ? 30 : 7);

        await AuthAuditWriter
            .TryAppendAsync(
                _auditLog,
                _logger,
                AuthAuditEventTypes.LoginSuccess,
                user.Id,
                request.SourceIp,
                request.UserAgent,
                new { tenantSlug = request.TenantSlug, rememberMe = request.RememberMe },
                request.CorrelationId,
                ct
            )
            .ConfigureAwait(false);

        return Result<LoginResponse>.Success(
            new LoginResponse(
                AccessToken: accessToken.Jwt,
                AccessTokenExpiresAt: accessToken.ExpiresAt,
                RefreshToken: refreshToken,
                RefreshTokenExpiresAt: refreshExpiresAt,
                Role: user.Role.ToString(),
                Email: user.Email
            )
        );
    }

    private Task EmitLoginFailedAsync(Guid? userId, LoginCommand request, CancellationToken ct) =>
        AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.LoginFailed,
            userId,
            request.SourceIp,
            request.UserAgent,
            new { reason = InvalidCredentials, submittedEmail = request.Email ?? string.Empty },
            request.CorrelationId,
            ct
        );
}
