using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 — logout handler unit tests. Pins the idempotency
/// discipline (R6 — logging out a session that's already gone is a
/// 200 not a 404, so the frontend can fire-and-forget without
/// special-casing the "logged out from another tab" race) + the
/// AllDevices flag routing. Sprint-12.5 U1 pins the
/// <c>auth.logout</c> audit-row emit.
/// </summary>
public sealed class LogoutCommandHandlerTests
{
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();

    private LogoutCommandHandler BuildHandler() =>
        new(_refreshStore, _auditLog, NullLogger<LogoutCommandHandler>.Instance);

    private static LogoutCommand Cmd(string token, bool allDevices, Guid userId) =>
        new(token, allDevices, userId, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    [Fact]
    public async Task SingleDevice_CallsRevokeAsync_EmitsLogoutAudit()
    {
        var userId = Guid.NewGuid();

        var result = await BuildHandler()
            .Handle(Cmd("refresh-token", false, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore
            .Received(1)
            .RevokeAsync("t1", userId, "refresh-token", Arg.Any<CancellationToken>());
        await _refreshStore
            .DidNotReceive()
            .RevokeAllForUserAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.Logout,
                userId,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AllDevicesTrue_CallsRevokeAllForUser_EmitsLogoutAudit()
    {
        var userId = Guid.NewGuid();

        var result = await BuildHandler()
            .Handle(Cmd("refresh-token", true, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore
            .Received(1)
            .RevokeAllForUserAsync("t1", userId, Arg.Any<CancellationToken>());
        await _refreshStore
            .DidNotReceive()
            .RevokeAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.Logout,
                userId,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task EmptyRefreshToken_ReturnsSuccessAsNoOp_NoAuditRow()
    {
        // Defensive: the controller validates input, but the handler
        // collapses empty to success so logout doesn't 500 on a
        // double-tap. No audit row — there's no logout to record.
        var result = await BuildHandler()
            .Handle(Cmd("", false, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore
            .DidNotReceive()
            .RevokeAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
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
}
