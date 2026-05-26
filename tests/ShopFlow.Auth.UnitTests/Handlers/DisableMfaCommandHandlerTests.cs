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
/// Sprint-12.5 U1 — pins the <c>auth.mfa.disabled</c> audit-row emit
/// on the successful disable path. Rejection paths
/// (<c>auth.invalid_credentials</c> / <c>auth.mfa_required_cannot_disable</c>)
/// do NOT audit.
/// </summary>
public sealed class DisableMfaCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITotpSecretRepository _secrets = Substitute.For<ITotpSecretRepository>();
    private readonly IRecoveryCodeRepository _recoveryCodes = Substitute.For<IRecoveryCodeRepository>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();

    private DisableMfaCommandHandler BuildHandler() => new(
        _users, _hasher, _secrets, _recoveryCodes, _auditLog,
        NullLogger<DisableMfaCommandHandler>.Instance);

    private static DisableMfaCommand Cmd(Guid userId, string currentPwd) =>
        new(userId, "t1", currentPwd, "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    [Fact]
    public async Task Happy_DisablesMfaAndEmitsAudit()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        user.MarkMfaEnrolled();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);

        var result = await BuildHandler().Handle(Cmd(user.Id, "password"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _secrets.Received(1).DeleteAsync(user.Id, Arg.Any<CancellationToken>());
        await _recoveryCodes.Received(1).DeleteAllAsync(user.Id, Arg.Any<CancellationToken>());
        await _auditLog.Received(1).AppendAsync(
            AuthAuditEventTypes.MfaDisabled,
            user.Id, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MfaRequiredUser_ReturnsCannotDisable_NoAuditRow()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        user.MarkMfaEnrolled();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);

        var result = await BuildHandler().Handle(Cmd(user.Id, "password"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.mfa_required_cannot_disable");
        await _auditLog.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WrongPassword_ReturnsInvalidCredentials_NoAuditRow()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("WRONG", ValidHash).Returns(false);

        var result = await BuildHandler().Handle(Cmd(user.Id, "WRONG"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _auditLog.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
