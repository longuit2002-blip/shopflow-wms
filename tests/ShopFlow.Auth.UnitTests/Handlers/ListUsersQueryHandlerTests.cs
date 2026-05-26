using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Application.Queries;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

public sealed class ListUsersQueryHandlerTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private ListUsersQueryHandler BuildHandler() => new(_users);

    [Fact]
    public async Task Happy_ReturnsProjectedSummaries()
    {
        var u1 = User.Create("alice@example.com", ValidHash, UserRole.Owner);
        var u2 = User.Create("bob@example.com", ValidHash, UserRole.Picker);
        _users
            .ListAsync(1, 25, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<User>>(new[] { u1, u2 }));

        var result = await BuildHandler().Handle(new ListUsersQuery(1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Users.Should().HaveCount(2);
        result.Value.Users[0].Email.Should().Be("alice@example.com");
        result.Value.Users[0].Role.Should().Be("Owner");
        result.Value.Users[1].Email.Should().Be("bob@example.com");
        result.Value.Users[1].Role.Should().Be("Picker");
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task EmptyResult_ReturnsEmptyUsersList()
    {
        _users
            .ListAsync(1, 25, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>()));

        var result = await BuildHandler().Handle(new ListUsersQuery(1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task NonPositivePage_ClampsToOne()
    {
        _users
            .ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>()));

        var result = await BuildHandler().Handle(new ListUsersQuery(0, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        await _users.Received(1).ListAsync(1, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OversizePageSize_ClampsToMax()
    {
        _users
            .ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>()));

        var result = await BuildHandler()
            .Handle(new ListUsersQuery(1, 500), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PageSize.Should().Be(100);
        await _users.Received(1).ListAsync(1, 100, Arg.Any<CancellationToken>());
    }
}
