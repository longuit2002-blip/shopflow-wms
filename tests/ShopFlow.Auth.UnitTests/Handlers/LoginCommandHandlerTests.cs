using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ShopFlow.Auth.Application;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Application;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 + Sprint-9 U8 — login handler unit tests. Sprint-9
/// adds the lockout state machine + MFA branch + AccountLockedV1
/// outbox emission on the lockout boundary.
/// </summary>
public sealed class LoginCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IMfaChallengeTokenCodec _codec = Substitute.For<IMfaChallengeTokenCodec>();
    private readonly IAuthOutbox _outbox = Substitute.For<IAuthOutbox>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly AuthLockoutOptions _lockout = new();

    private LoginCommandHandler BuildHandler() => new(
        _users, _hasher, _issuer, _refreshStore, _codec, _outbox, _clock,
        Options.Create(_lockout), _requestContext);

    private void StubIssuerHappyPath(User user, string tenantSlug)
    {
        _issuer
            .IssueAccessTokenAsync(user, tenantSlug, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AccessToken("jwt-bytes", DateTime.UtcNow.AddMinutes(15))));
        _refreshStore
            .IssueAsync(tenantSlug, user.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("opaque-refresh"));
    }

    [Fact]
    public async Task Happy_ValidCredentials_NoMfa_ReturnsTokenPairAndStampsLogin()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);
        StubIssuerHappyPath(user, "t1");

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("jwt-bytes");
        result.Value.RefreshToken.Should().Be("opaque-refresh");
        result.Value.Role.Should().Be("Picker");
        result.Value.Email.Should().Be("alice@example.com");
        result.Value.MfaRequired.Should().BeFalse();
        result.Value.MfaEnrollmentRequired.Should().BeFalse();
        user.LastLoginAt.Should().NotBeNull(because: "RecordLogin was called");
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingUser_ReturnsInvalidCredentials_NoEnumeration()
    {
        _users.GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var result = await BuildHandler().Handle(
            new LoginCommand("ghost@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task InactiveUser_ReturnsInvalidCredentials()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        user.Deactivate();
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task WrongPassword_ReturnsInvalidCredentialsAndIncrementsFailures()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("wrong", ValidHash).Returns(false);

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "wrong", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        user.FailedLoginCount.Should().Be(1);
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        // No AccountLockedV1 emission yet — only 1 failure.
        await _outbox.DidNotReceive().AppendAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FifthWrongPassword_TriggersLockoutAndEmitsAccountLocked()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("wrong", ValidHash).Returns(false);

        var handler = BuildHandler();
        for (var i = 0; i < 5; i++)
        {
            await handler.Handle(
                new LoginCommand("alice@example.com", "wrong", false, "t1"),
                CancellationToken.None);
        }

        user.LockedUntil.Should().NotBeNull();
        await _outbox.Received(1).AppendAsync(
            Arg.Is<string>(t => t.Contains("AccountLockedV1")),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyLocked_ReturnsInvalidCredentials_SilentlyNoExtension()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(_clock, 5, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        }
        var originalLockedUntil = user.LockedUntil;
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);  // even correct password fails

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        user.LockedUntil.Should().Be(originalLockedUntil);
    }

    [Fact]
    public async Task MfaRequiredAndEnrolled_ReturnsMfaChallengeToken()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        user.MarkMfaEnrolled();
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);
        _codec.Issue(user.Id, "t1", false, MfaChallengeIntent.Challenge, Arg.Any<DateTime>())
            .Returns("CHALLENGE-TOKEN");

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MfaRequired.Should().BeTrue();
        result.Value.MfaChallengeToken.Should().Be("CHALLENGE-TOKEN");
        result.Value.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task OwnerNotEnrolled_ReturnsMfaEnrollmentToken()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        // Owner is MfaRequired=true by Create default; MfaEnrolled=false
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);
        _codec.Issue(user.Id, "t1", false, MfaChallengeIntent.Enrollment, Arg.Any<DateTime>())
            .Returns("ENROLL-TOKEN");

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MfaEnrollmentRequired.Should().BeTrue();
        result.Value.MfaEnrollmentToken.Should().Be("ENROLL-TOKEN");
        result.Value.AccessToken.Should().BeNull();
    }
}
