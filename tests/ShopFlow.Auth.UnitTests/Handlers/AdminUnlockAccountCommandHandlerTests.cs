using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-12.5 U1 — pins the <c>auth.account.unlocked_by_owner</c>
/// audit-row emit. Audit row's userId is the ACTOR; metadata.targetUserId
/// identifies the subject of the unlock. <c>auth.target_not_found</c>
/// rejection does NOT audit.
/// </summary>
public sealed class AdminUnlockAccountCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();

    private AdminUnlockAccountCommandHandler BuildHandler() => new(
        _users, _auditLog, NullLogger<AdminUnlockAccountCommandHandler>.Instance);

    [Fact]
    public async Task Happy_UnlocksTargetAndEmitsAuditAttributedToActor()
    {
        var actorId = Guid.NewGuid();
        var target = User.Create("bob@example.com", ValidHash, UserRole.Picker);
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(target));

        var cmd = new AdminUnlockAccountCommand(
            actorId, target.Id, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());
        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _auditLog.Received(1).AppendAsync(
            AuthAuditEventTypes.AccountUnlockedByOwner,
            actorId,
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains(target.Id.ToString(), StringComparison.Ordinal)),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TargetNotFound_ReturnsFailure_NoAuditRow()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var cmd = new AdminUnlockAccountCommand(
            Guid.NewGuid(), Guid.NewGuid(), "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());
        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.target_not_found");
        await _auditLog.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
