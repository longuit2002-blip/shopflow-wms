using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 — logout handler unit tests. Pins the idempotency
/// discipline (R6 — logging out a session that's already gone is a
/// 200 not a 404, so the frontend can fire-and-forget without
/// special-casing the "logged out from another tab" race) + the
/// AllDevices flag routing.
/// </summary>
public sealed class LogoutCommandHandlerTests
{
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();

    private LogoutCommandHandler BuildHandler() => new(_refreshStore);

    [Fact]
    public async Task SingleDevice_CallsRevokeAsync()
    {
        var userId = Guid.NewGuid();

        var result = await BuildHandler().Handle(
            new LogoutCommand("refresh-token", false, userId, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore.Received(1).RevokeAsync("t1", userId, "refresh-token", Arg.Any<CancellationToken>());
        await _refreshStore.DidNotReceive().RevokeAllForUserAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllDevicesTrue_CallsRevokeAllForUser()
    {
        var userId = Guid.NewGuid();

        var result = await BuildHandler().Handle(
            new LogoutCommand("refresh-token", true, userId, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore.Received(1).RevokeAllForUserAsync("t1", userId, Arg.Any<CancellationToken>());
        await _refreshStore.DidNotReceive().RevokeAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyRefreshToken_ReturnsSuccessAsNoOp()
    {
        // Defensive: the controller validates input, but the handler
        // collapses empty to success so logout doesn't 500 on a
        // double-tap.
        var result = await BuildHandler().Handle(
            new LogoutCommand("", false, Guid.NewGuid(), "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore.DidNotReceive().RevokeAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
