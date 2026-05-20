using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 — refresh handler unit tests. Covers all 4 outcomes
/// of <see cref="IRefreshTokenStore.RotateAsync"/> + the
/// deactivated-mid-session edge case.
/// </summary>
public sealed class RefreshTokenCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();

    private RefreshTokenCommandHandler BuildHandler() => new(_refreshStore, _users, _issuer);

    [Fact]
    public async Task LiveToken_RotatesAndReturnsNewPair()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        _refreshStore.RotateAsync("t1", user.Id, "old-refresh", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.Issued, "new-refresh")));
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _issuer.IssueAccessToken(user, "t1")
            .Returns(new AccessToken("new-jwt", DateTime.UtcNow.AddMinutes(15)));

        var result = await BuildHandler().Handle(
            new RefreshTokenCommand("old-refresh", user.Id, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("new-jwt");
        result.Value.RefreshToken.Should().Be("new-refresh");
    }

    [Fact]
    public async Task GraceReplay_ReturnsSameSuccessor_AsIssuedBranch()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _refreshStore.RotateAsync("t1", user.Id, "old-refresh", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.GraceReplay, "successor-refresh")));
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _issuer.IssueAccessToken(user, "t1")
            .Returns(new AccessToken("new-jwt", DateTime.UtcNow.AddMinutes(15)));

        var result = await BuildHandler().Handle(
            new RefreshTokenCommand("old-refresh", user.Id, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RefreshToken.Should().Be("successor-refresh");
    }

    [Fact]
    public async Task NotFound_ReturnsInvalidCredentials()
    {
        _refreshStore.RotateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.NotFound, null)));

        var result = await BuildHandler().Handle(
            new RefreshTokenCommand("expired", Guid.NewGuid(), "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task ReuseDetected_ReturnsRefreshReused()
    {
        _refreshStore.RotateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.ReuseDetected, null)));

        var result = await BuildHandler().Handle(
            new RefreshTokenCommand("replayed", Guid.NewGuid(), "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.refresh_reused");
    }

    [Fact]
    public async Task DeactivatedMidSession_RevokesAllAndReturnsInvalidCredentials()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        user.Deactivate();
        _refreshStore.RotateAsync("t1", user.Id, "old", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.Issued, "new")));
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));

        var result = await BuildHandler().Handle(
            new RefreshTokenCommand("old", user.Id, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _refreshStore.Received(1).RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyRefreshToken_ReturnsInvalidCredentials(string token)
    {
        var result = await BuildHandler().Handle(
            new RefreshTokenCommand(token, Guid.NewGuid(), "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _refreshStore.DidNotReceive().RotateAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
