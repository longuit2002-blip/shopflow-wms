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
/// Sprint-12.5 U1 — pins the <c>auth.mfa.reset_by_owner</c> audit-row
/// emit. Audit row's userId is the ACTOR; metadata.targetUserId
/// identifies the subject of the reset. Rejection paths
/// (<c>auth.target_not_found</c> / <c>auth.mfa_required_invariant_owner</c>)
/// do NOT audit.
/// </summary>
public sealed class AdminMfaResetCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITotpSecretRepository _secrets = Substitute.For<ITotpSecretRepository>();
    private readonly IRecoveryCodeRepository _recoveryCodes = Substitute.For<IRecoveryCodeRepository>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();

    private AdminMfaResetCommandHandler BuildHandler() => new(
        _users, _secrets, _recoveryCodes, _auditLog,
        NullLogger<AdminMfaResetCommandHandler>.Instance);

    [Fact]
    public async Task Happy_ResetsTargetAndEmitsAuditAttributedToActor()
    {
        var actorId = Guid.NewGuid();
        var target = User.Create("bob@example.com", ValidHash, UserRole.Picker);
        target.MarkMfaEnrolled();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(target));

        var cmd = new AdminMfaResetCommand(
            actorId, target.Id, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());
        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _secrets.Received(1).DeleteAsync(target.Id, Arg.Any<CancellationToken>());
        await _auditLog.Received(1).AppendAsync(
            AuthAuditEventTypes.MfaResetByOwner,
            actorId, // ACTOR, not target
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains(target.Id.ToString(), StringComparison.Ordinal)),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TargetNotFound_ReturnsFailure_NoAuditRow()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var cmd = new AdminMfaResetCommand(
            Guid.NewGuid(), Guid.NewGuid(), "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());
        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.target_not_found");
        await _auditLog.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TargetIsOwnerWithMfaRequired_ReturnsInvariantOwner_NoAuditRow()
    {
        var target = User.Create("owner@example.com", ValidHash, UserRole.Owner);
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(target));

        var cmd = new AdminMfaResetCommand(
            Guid.NewGuid(), target.Id, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());
        var result = await BuildHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.mfa_required_invariant_owner");
        await _auditLog.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
