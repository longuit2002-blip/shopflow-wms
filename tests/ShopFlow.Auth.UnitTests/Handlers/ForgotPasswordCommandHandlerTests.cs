using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ShopFlow.Auth.Application;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-12.5 U1 — pins the <c>auth.password.reset.requested</c>
/// audit-row emit. The R6 always-200 silent-skip / unknown-email
/// paths do NOT audit (audit captures real reset tokens emitted, not
/// every anonymous probe).
/// </summary>
public sealed class ForgotPasswordCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenRepository _resetTokens =
        Substitute.For<IPasswordResetTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IAuthOutbox _outbox = Substitute.For<IAuthOutbox>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();
    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)
    );

    private readonly AuthPasswordResetOptions _options = new()
    {
        CooldownMinutes = 5,
        TokenTtlMinutes = 30,
        SyntheticHash = ValidHash,
        WorkspaceUrlTemplate = "https://{slug}.shopflow.test",
    };

    private ForgotPasswordCommandHandler BuildHandler() =>
        new(
            _users,
            _resetTokens,
            _hasher,
            _outbox,
            _auditLog,
            NullLogger<ForgotPasswordCommandHandler>.Instance,
            _requestContext,
            _clock,
            Options.Create(_options)
        );

    private static ForgotPasswordCommand Cmd(string email) =>
        new(email, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    [Fact]
    public async Task UnknownEmail_ReturnsSuccess_NoAuditRow()
    {
        _users
            .GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var result = await BuildHandler().Handle(Cmd("ghost@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
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

    [Fact]
    public async Task KnownActiveUser_EmitsResetTokenAndAudit()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _users
            .GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _resetTokens
            .GetLastIssuedAtAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DateTime?>(null));
        _resetTokens
            .AddAsync(Arg.Any<byte[]>(), user.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var result = await BuildHandler().Handle(Cmd("alice@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.PasswordResetRequested,
                user.Id,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CooldownActive_SilentSkip_NoAuditRow()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _users
            .GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        // Cooldown is 5min; last issued 1min ago.
        _resetTokens
            .GetLastIssuedAtAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DateTime?>(_clock.GetUtcNow().UtcDateTime.AddMinutes(-1)));

        var result = await BuildHandler().Handle(Cmd("alice@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
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
