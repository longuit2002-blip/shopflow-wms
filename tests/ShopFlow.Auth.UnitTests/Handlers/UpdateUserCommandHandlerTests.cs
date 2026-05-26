using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Application.Services;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Domain.Events;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U8 — single-test-file covering the three discriminator
/// branches of the consolidated <see cref="UpdateUserCommand"/>
/// (KTD8). Each branch has its own behavioural contract; the suite
/// pins them all in one place so a future refactor that splits the
/// handler back into three has a single migration target.
/// </summary>
public sealed class UpdateUserCommandHandlerTests
{
    private const string CurrentHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";
    private const string NewHash = "$argon2id$v=19$m=65536,t=4,p=4$bmV3$bmV3aGFzaA";
    private const string TempPwd = "GENERATED-new-456";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IPasswordGenerator _generator = Substitute.For<IPasswordGenerator>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();

    private UpdateUserCommandHandler BuildHandler() =>
        new(_users, _hasher, _generator, _refreshStore);

    private User Setup(UserRole role = UserRole.Owner)
    {
        var user = User.Create("alice@example.com", CurrentHash, role);
        user.ClearDomainEvents();
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        return user;
    }

    // ───────────── SetRole branch ─────────────

    [Fact]
    public async Task SetRole_ChangesRoleAndRaisesEventAndPersists()
    {
        var user = Setup(UserRole.Owner);

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(user.Id, UpdateUserOperation.SetRole, "Picker", "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        user.Role.Should().Be(UserRole.Picker);
        user.DomainEvents.Should().ContainSingle(e => e is UserRoleChangedEvent);
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        await _refreshStore
            .DidNotReceive()
            .RevokeAllForUserAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetRole_NoOpWhenSameRole_StillReturnsSuccess()
    {
        var user = Setup(UserRole.Picker);

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(user.Id, UpdateUserOperation.SetRole, "Picker", "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        user.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData(null)]
    [InlineData("")]
    public async Task SetRole_RejectsInvalidRoleString(string? role)
    {
        var user = Setup();

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(user.Id, UpdateUserOperation.SetRole, role, "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("users.role_invalid");
    }

    // ───────────── ResetPassword branch ─────────────

    [Fact]
    public async Task ResetPassword_RotatesHashRevokesSessionsAndReturnsTempPassword()
    {
        var user = Setup();
        _generator.Generate().Returns(TempPwd);
        _hasher.Hash(TempPwd).Returns(NewHash);

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(user.Id, UpdateUserOperation.ResetPassword, null, "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be(NewHash);
        result.Value!.ResetPassword.Should().NotBeNull();
        result.Value.ResetPassword!.TemporaryPassword.Should().Be(TempPwd);
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        await _refreshStore
            .Received(1)
            .RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
    }

    // ───────────── Deactivate branch ─────────────

    [Fact]
    public async Task Deactivate_FlipsIsActiveRevokesSessionsAndReturnsSuccess()
    {
        var user = Setup();

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(user.Id, UpdateUserOperation.Deactivate, null, "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        await _refreshStore
            .Received(1)
            .RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_IsIdempotent()
    {
        var user = Setup();
        user.Deactivate();
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(user.Id, UpdateUserOperation.Deactivate, null, "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        // Revoke-all still fires — idempotent on the store side.
        await _refreshStore
            .Received(1)
            .RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
    }

    // ───────────── Common failure ─────────────

    [Fact]
    public async Task MissingUser_ReturnsUsersNotFound()
    {
        _users
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var result = await BuildHandler()
            .Handle(
                new UpdateUserCommand(Guid.NewGuid(), UpdateUserOperation.SetRole, "Owner", "t1"),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("users.not_found");
    }
}
