using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ShopFlow.Auth.Application;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Application;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-12.5 U1 — pins the <c>auth.mfa.enrolled</c> audit-row emit
/// on the successful enrollment-verify path. Token/OTP rejection paths
/// do NOT audit.
/// </summary>
public sealed class VerifyEnrollMfaCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMfaChallengeTokenCodec _codec = Substitute.For<IMfaChallengeTokenCodec>();
    private readonly IEnrollmentSecretStore _enrollmentStore =
        Substitute.For<IEnrollmentSecretStore>();
    private readonly ITotpProvider _totp = Substitute.For<ITotpProvider>();
    private readonly ITotpSecretCipher _cipher = Substitute.For<ITotpSecretCipher>();
    private readonly ITotpSecretRepository _secrets = Substitute.For<ITotpSecretRepository>();
    private readonly IRecoveryCodeRepository _recoveryCodes =
        Substitute.For<IRecoveryCodeRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IAuthOutbox _outbox = Substitute.For<IAuthOutbox>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();
    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)
    );

    private VerifyEnrollMfaCommandHandler BuildHandler() =>
        new(
            _users,
            _codec,
            _enrollmentStore,
            _totp,
            _cipher,
            _secrets,
            _recoveryCodes,
            _hasher,
            _issuer,
            _refreshStore,
            _outbox,
            _auditLog,
            NullLogger<VerifyEnrollMfaCommandHandler>.Instance,
            _requestContext,
            _clock
        );

    [Fact]
    public async Task InvalidEnrollmentToken_ReturnsInvalidCredentials_NoAuditRow()
    {
        _codec
            .TryDecode(Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns((MfaChallengePayload?)null);

        var cmd = new VerifyEnrollMfaCommand(
            UserId: Guid.NewGuid(),
            TenantSlug: "t1",
            EnrollmentToken: "bad-token",
            EnrollmentId: Guid.NewGuid(),
            Otp: "123456",
            RememberMe: false,
            SourceIp: "203.0.113.10",
            UserAgent: "test-ua/1.0",
            CorrelationId: Guid.NewGuid()
        );

        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _auditLog
            .DidNotReceive()
            .AppendAsync(
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Happy_EnrollsAndEmitsMfaEnrolledAudit()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        var enrollmentId = Guid.NewGuid();
        _codec
            .TryDecode(Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(
                new MfaChallengePayload(
                    UserId: user.Id,
                    TenantSlug: "t1",
                    RememberMe: false,
                    Intent: MfaChallengeIntent.Enrollment
                )
            );
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        var rawSecret = new byte[32];
        _enrollmentStore
            .ConsumeAsync("t1", user.Id, enrollmentId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(rawSecret));
        _totp
            .VerifyOtp(rawSecret, "123456", _clock)
            .Returns(new OtpVerificationResult(IsValid: true, TimeStep: 12345L));
        _cipher.Encrypt(rawSecret, Arg.Any<Guid>(), user.Id).Returns((new byte[64], 1));
        _hasher.Hash(Arg.Any<string>(), Argon2Profile.RecoveryCode).Returns(ValidHash);
        _recoveryCodes
            .AddBatchAsync(user.Id, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ShopFlow.SharedKernel.Domain.Result.Success()));
        _issuer
            .IssueAccessTokenAsync(user, "t1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AccessToken("jwt", DateTime.UtcNow.AddMinutes(15))));
        _refreshStore
            .IssueAsync("t1", user.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("opaque-refresh"));

        var cmd = new VerifyEnrollMfaCommand(
            UserId: user.Id,
            TenantSlug: "t1",
            EnrollmentToken: "token",
            EnrollmentId: enrollmentId,
            Otp: "123456",
            RememberMe: false,
            SourceIp: "203.0.113.10",
            UserAgent: "test-ua/1.0",
            CorrelationId: Guid.NewGuid()
        );

        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.MfaEnrolled,
                user.Id,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }
}
