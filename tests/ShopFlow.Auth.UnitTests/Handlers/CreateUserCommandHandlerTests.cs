using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Application.Services;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

public sealed class CreateUserCommandHandlerTests
{
    private const string HashOut = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";
    private const string TempPassword = "GENERATED-temp-123";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IPasswordGenerator _generator = Substitute.For<IPasswordGenerator>();

    private CreateUserCommandHandler BuildHandler()
    {
        _generator.Generate().Returns(TempPassword);
        _hasher.Hash(TempPassword).Returns(HashOut);
        return new CreateUserCommandHandler(_users, _hasher, _generator);
    }

    [Fact]
    public async Task Happy_CreatesUserAndReturnsTemporaryPassword()
    {
        _users
            .AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<User>.Success(call.Arg<User>()));

        var result = await BuildHandler()
            .Handle(new CreateUserCommand("invitee@example.com", "Picker"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("invitee@example.com");
        result.Value.Role.Should().Be("Picker");
        result.Value.TemporaryPassword.Should().Be(TempPassword);
        await _users
            .Received(1)
            .AddAsync(
                Arg.Is<User>(u => u.PasswordHash == HashOut && u.Role == UserRole.Picker),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DuplicateEmail_ReturnsAuthEmailInUseFromRepoResult()
    {
        _users
            .AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result<User>.Failure(
                        "A user with that email already exists.",
                        "auth.email_in_use"
                    )
                )
            );

        var result = await BuildHandler()
            .Handle(new CreateUserCommand("dup@example.com", "Owner"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.email_in_use");
    }

    [Fact]
    public async Task InvalidEmail_ReturnsUsersEmailInvalid()
    {
        var result = await BuildHandler()
            .Handle(new CreateUserCommand("not-an-email", "Owner"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("users.email_invalid");
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("owner")] // lowercase
    [InlineData("ADMIN")] // unknown
    [InlineData("")]
    public async Task InvalidRole_ReturnsUsersRoleInvalid(string role)
    {
        var result = await BuildHandler()
            .Handle(new CreateUserCommand("user@example.com", role), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("users.role_invalid");
    }
}
