using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Application;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-12.5 U1 — pins the <c>auth.mfa.used</c> audit-row emit on
/// the successful second-factor verify path. Token/OTP rejection
/// paths do NOT audit.
/// </summary>
public sealed class VerifyMfaCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IMfaChallengeTokenCodec _codec = Substitute.For<IMfaChallengeTokenCodec>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITotpSecretRepository _secrets = Substitute.For<ITotpSecretRepository>();
    private readonly ITotpProvider _totp = Substitute.For<ITotpProvider>();
    private readonly ITotpSecretCipher _cipher = Substitute.For<ITotpSecretCipher>();
    private readonly IRecoveryCodeRepository _recoveryCodes =
        Substitute.For<IRecoveryCodeRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();
    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)
    );

    private VerifyMfaCommandHandler BuildHandler() =>
        new(
            _codec,
            _users,
            _secrets,
            _totp,
            _cipher,
            _recoveryCodes,
            _hasher,
            _issuer,
            _refreshStore,
            _auditLog,
            NullLogger<VerifyMfaCommandHandler>.Instance,
            _requestContext,
            _clock
        );

    private static VerifyMfaCommand Cmd(
        string challenge,
        string? otp = "123456",
        string? recovery = null
    ) => new(challenge, otp, recovery, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    [Fact]
    public async Task InvalidChallengeToken_ReturnsInvalidCredentials_NoAuditRow()
    {
        _codec
            .TryDecode(Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns((MfaChallengePayload?)null);

        var result = await BuildHandler().Handle(Cmd("bad-token"), CancellationToken.None);

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
    public async Task Happy_OtpVerified_EmitsMfaUsedAudit()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        user.MarkMfaEnrolled();
        _codec
            .TryDecode(Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(new MfaChallengePayload(user.Id, "t1", false, MfaChallengeIntent.Challenge));
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        var view = new TotpSecretView(new byte[64], 1, LastUsedTimeStep: null);
        _secrets
            .GetAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TotpSecretView?>(view));
        _cipher
            .Decrypt(view.EncryptedSecret, view.KeyId, Arg.Any<Guid>(), user.Id)
            .Returns(new byte[32]);
        _totp
            .VerifyOtp(Arg.Any<byte[]>(), "123456", _clock)
            .Returns(new OtpVerificationResult(IsValid: true, TimeStep: 12345L));
        _issuer
            .IssueAccessTokenAsync(user, "t1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AccessToken("jwt", DateTime.UtcNow.AddMinutes(15))));
        _refreshStore
            .IssueAsync("t1", user.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("opaque-refresh"));

        var result = await BuildHandler().Handle(Cmd("challenge"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.MfaUsed,
                user.Id,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }
}
