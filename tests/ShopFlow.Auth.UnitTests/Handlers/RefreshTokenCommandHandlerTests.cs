using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Application;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 — refresh handler unit tests. Covers all 4 outcomes
/// of <see cref="IRefreshTokenStore.RotateAsync"/> + the
/// deactivated-mid-session edge case. Sprint-12.5 U1 pins the
/// <c>auth.refresh.success</c> / <c>auth.refresh.reused</c> audit emits.
/// </summary>
public sealed class RefreshTokenCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();
    private readonly IAuthOutbox _outbox = Substitute.For<IAuthOutbox>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();

    private RefreshTokenCommandHandler BuildHandler() =>
        new(
            _refreshStore,
            _users,
            _issuer,
            _outbox,
            _auditLog,
            NullLogger<RefreshTokenCommandHandler>.Instance,
            _requestContext
        );

    private static RefreshTokenCommand Cmd(string token, Guid userId) =>
        new(token, userId, "t1", "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    [Fact]
    public async Task LiveToken_RotatesAndReturnsNewPair_EmitsRefreshSuccess()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Picker);
        _refreshStore
            .RotateAsync("t1", user.Id, "old-refresh", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.Issued, "new-refresh"))
            );
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _issuer
            .IssueAccessTokenAsync(user, "t1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AccessToken("new-jwt", DateTime.UtcNow.AddMinutes(15))));

        var result = await BuildHandler()
            .Handle(Cmd("old-refresh", user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("new-jwt");
        result.Value.RefreshToken.Should().Be("new-refresh");
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.RefreshSuccess,
                user.Id,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<string>(s => s.Contains("chainId", StringComparison.Ordinal)),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GraceReplay_ReturnsSameSuccessor_AsIssuedBranch_EmitsRefreshSuccess()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _refreshStore
            .RotateAsync("t1", user.Id, "old-refresh", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new RefreshRotateResult(RefreshRotateOutcome.GraceReplay, "successor-refresh")
                )
            );
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _issuer
            .IssueAccessTokenAsync(user, "t1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AccessToken("new-jwt", DateTime.UtcNow.AddMinutes(15))));

        var result = await BuildHandler()
            .Handle(Cmd("old-refresh", user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RefreshToken.Should().Be("successor-refresh");
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.RefreshSuccess,
                user.Id,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task NotFound_ReturnsInvalidCredentials_NoAuditRow()
    {
        _refreshStore
            .RotateAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.NotFound, null)));

        var result = await BuildHandler()
            .Handle(Cmd("expired", Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
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
    public async Task ReuseDetected_ReturnsRefreshReused_EmitsRefreshReusedAudit()
    {
        _refreshStore
            .RotateAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.ReuseDetected, null))
            );
        var userId = Guid.NewGuid();

        var result = await BuildHandler().Handle(Cmd("replayed", userId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.refresh_reused");
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.RefreshReused,
                userId,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<string>(s =>
                    s.Contains("chainId", StringComparison.Ordinal)
                    && s.Contains("revokedAt", StringComparison.Ordinal)
                ),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeactivatedMidSession_RevokesAllAndReturnsInvalidCredentials()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        user.Deactivate();
        _refreshStore
            .RotateAsync("t1", user.Id, "old", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RefreshRotateResult(RefreshRotateOutcome.Issued, "new")));
        _users
            .GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));

        var result = await BuildHandler().Handle(Cmd("old", user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _refreshStore
            .Received(1)
            .RevokeAllForUserAsync("t1", user.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyRefreshToken_ReturnsInvalidCredentials(string token)
    {
        var result = await BuildHandler()
            .Handle(Cmd(token, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _refreshStore
            .DidNotReceive()
            .RotateAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
