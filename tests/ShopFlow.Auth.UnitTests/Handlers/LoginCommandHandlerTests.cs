using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-8 U7 — login handler unit tests. The handler composes 4
/// ports; this suite pins the enumeration-prevention discipline (R6)
/// + the rememberMe TTL bucket propagation + the LastLoginAt
/// persistence side-effect.
/// </summary>
public sealed class LoginCommandHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _issuer = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenStore _refreshStore = Substitute.For<IRefreshTokenStore>();

    private LoginCommandHandler BuildHandler() => new(_users, _hasher, _issuer, _refreshStore);

    private void StubIssuerHappyPath(User user, string tenantSlug)
    {
        _issuer
            .IssueAccessTokenAsync(user, tenantSlug, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AccessToken("jwt-bytes", DateTime.UtcNow.AddMinutes(15))));
        _refreshStore
            .IssueAsync(tenantSlug, user.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("opaque-refresh"));
    }

    [Fact]
    public async Task Happy_ValidCredentials_ReturnsTokenPairAndStampsLogin()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);
        StubIssuerHappyPath(user, "t1");

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("jwt-bytes");
        result.Value.RefreshToken.Should().Be("opaque-refresh");
        result.Value.Role.Should().Be("Owner");
        result.Value.Email.Should().Be("alice@example.com");
        user.LastLoginAt.Should().NotBeNull(because: "RecordLogin was called");
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingUser_ReturnsInvalidCredentials_NoEnumeration()
    {
        _users.GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));

        var result = await BuildHandler().Handle(
            new LoginCommand("ghost@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task InactiveUser_ReturnsInvalidCredentials_SameAsMissing()
    {
        var user = User.Create("disabled@example.com", ValidHash, UserRole.Owner);
        user.Deactivate();
        _users.GetByEmailAsync("disabled@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));

        var result = await BuildHandler().Handle(
            new LoginCommand("disabled@example.com", "password", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task WrongPassword_ReturnsInvalidCredentials_SameAsMissing()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("WRONG", ValidHash).Returns(false);

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "WRONG", false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task RememberMeTrue_PropagatesToRefreshStoreIssue()
    {
        var user = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        _users.GetByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        _hasher.Verify("password", ValidHash).Returns(true);
        StubIssuerHappyPath(user, "t1");

        var result = await BuildHandler().Handle(
            new LoginCommand("alice@example.com", "password", true, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _refreshStore.Received(1)
            .IssueAsync("t1", user.Id, true, Arg.Any<CancellationToken>());
        result.Value!.RefreshTokenExpiresAt.Should().BeCloseTo(
            DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("email@example.com", "")]
    [InlineData(" ", "password")]
    public async Task EmptyInputs_ReturnInvalidCredentials_WithoutRepoLookup(
        string email, string password)
    {
        var result = await BuildHandler().Handle(
            new LoginCommand(email, password, false, "t1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.invalid_credentials");
        await _users.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
