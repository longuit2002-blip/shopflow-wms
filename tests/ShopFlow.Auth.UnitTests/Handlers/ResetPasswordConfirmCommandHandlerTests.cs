using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-12.5 U1 — pins the <c>auth.password.reset.completed</c>
/// audit-row emit on the successful confirm path.
/// </summary>
public sealed class ResetPasswordConfirmCommandHandlerTests
{
    private const string OldHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";
    private const string NewHash = "$argon2id$v=19$m=65536,t=4,p=4$bmV3$bmV3aGFzaA";

    private readonly IPasswordResetTokenRepository _resetTokens =
        Substitute.For<IPasswordResetTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();
    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)
    );

    private ResetPasswordConfirmCommandHandler BuildHandler() =>
        new(
            _resetTokens,
            _users,
            _hasher,
            _refreshStore,
            _auditLog,
            NullLogger<ResetPasswordConfirmCommandHandler>.Instance,
            _clock
        );

    private static ResetPasswordConfirmCommand Cmd(string token, string newPwd) =>
        new(token, newPwd, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    [Fact]
    public async Task Happy_ConsumesTokenRotatesAndEmitsAudit()
    {
        var user = User.Create("alice@example.com", OldHash, UserRole.Owner);
        _resetTokens
            .TryConsumeAsync(Arg.Any<byte[]>(), _clock, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<Guid>.Success(user.Id)));
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Hash("newPassword1").Returns(NewHash);

        var result = await BuildHandler()
            .Handle(Cmd("rawtoken", "newPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore
            .Received(1)
            .RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.PasswordResetCompleted,
                user.Id,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ConsumeFails_ReturnsInvalidCredentials_NoAuditRow()
    {
        _resetTokens
            .TryConsumeAsync(Arg.Any<byte[]>(), _clock, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<Guid>.Failure("invalid", "auth.invalid_token")));

        var result = await BuildHandler()
            .Handle(Cmd("rawtoken", "newPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
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

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public async Task NewPasswordBelowMinLength_ReturnsPasswordTooShort(string newPwd)
    {
        var result = await BuildHandler().Handle(Cmd("rawtoken", newPwd), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.password_too_short");
    }
}
