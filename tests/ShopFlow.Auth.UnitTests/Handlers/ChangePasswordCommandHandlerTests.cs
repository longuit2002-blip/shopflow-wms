using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 — change-password handler unit tests. Pins R15
/// (current-password gate) + R10 (post-change revoke-all-sessions
/// cascade) + the min-length validator.
/// </summary>
public sealed class ChangePasswordCommandHandlerTests
{
    private const string CurrentHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";
    private const string NewHash = "$argon2id$v=19$m=65536,t=4,p=4$bmV3$bmV3aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();

    private ChangePasswordCommandHandler BuildHandler() => new(_users, _hasher, _refreshStore);

    [Fact]
    public async Task Happy_RotatesHashAndRevokesAllSessions()
    {
        var user = User.Create("alice@example.com", CurrentHash, UserRole.Owner);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("oldPassword1", CurrentHash).Returns(true);
        _hasher.Hash("newPassword1").Returns(NewHash);

        var result = await BuildHandler().Handle(
            new ChangePasswordCommand("oldPassword1", "newPassword1", user.Id, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be(NewHash);
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        await _refreshStore.Received(1).RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WrongCurrentPassword_ReturnsInvalidCredentialsDoesNotRotate()
    {
        var user = User.Create("alice@example.com", CurrentHash, UserRole.Owner);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("WRONG", CurrentHash).Returns(false);

        var result = await BuildHandler().Handle(
            new ChangePasswordCommand("WRONG", "newPassword1", user.Id, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        user.PasswordHash.Should().Be(CurrentHash);
        await _users.DidNotReceive().UpdateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _refreshStore.DidNotReceive().RevokeAllForUserAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")] // 7 chars — one below threshold
    public async Task NewPasswordBelowMinLength_ReturnsPasswordTooShort(string newPwd)
    {
        var userId = Guid.NewGuid();

        var result = await BuildHandler().Handle(
            new ChangePasswordCommand("oldPassword1", newPwd, userId, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.password_too_short");
        // Validation fires BEFORE the repo lookup.
        await _users.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingUser_ReturnsInvalidCredentials()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var result = await BuildHandler().Handle(
            new ChangePasswordCommand("oldPassword1", "newPassword1", Guid.NewGuid(), "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task InactiveUser_ReturnsInvalidCredentials()
    {
        var user = User.Create("alice@example.com", CurrentHash, UserRole.Owner);
        user.Deactivate();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));

        var result = await BuildHandler().Handle(
            new ChangePasswordCommand("oldPassword1", "newPassword1", user.Id, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }
}
