using FluentAssertions;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Domain.Events;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Domain;

/// <summary>
/// Sprint-8 U1 — User aggregate behaviour contract. Pins factory validation
/// + the named mutation methods (UpdatePassword, SetRole, Deactivate,
/// RecordLogin) + the 3 domain events the aggregate raises. The
/// Application layer / IPasswordHasher / IUserRepository wrap this
/// aggregate; none of those concerns leak into the tests here.
/// </summary>
public sealed class UserTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    [Fact]
    public void Create_HappyPath_SetsFieldsAndRaisesUserCreatedEvent()
    {
        var user = User.Create("Operator@example.com", ValidHash, UserRole.Owner);

        user.Email.Should().Be("operator@example.com"); // normalized to lower
        user.PasswordHash.Should().Be(ValidHash);
        user.Role.Should().Be(UserRole.Owner);
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();

        user.DomainEvents.Should().HaveCount(1);
        user.DomainEvents[0].Should().BeOfType<UserCreatedEvent>();
        var evt = (UserCreatedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
        evt.Email.Should().Be("operator@example.com");
        evt.Role.Should().Be(UserRole.Owner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsEmptyEmail(string? email)
    {
        var act = () => User.Create(email!, ValidHash, UserRole.Owner);
        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "email");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("user@")]
    [InlineData("@example.com")]
    [InlineData("user@example")]
    public void Create_RejectsMalformedEmail(string email)
    {
        var act = () => User.Create(email, ValidHash, UserRole.Owner);
        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "email");
    }

    [Fact]
    public void Create_RejectsEmailLongerThan254Chars()
    {
        var longLocal = new string('a', 250);
        var email = $"{longLocal}@example.com"; // > 254 total
        var act = () => User.Create(email, ValidHash, UserRole.Owner);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "email");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_RejectsEmptyPasswordHash(string? hash)
    {
        var act = () => User.Create("user@example.com", hash!, UserRole.Owner);
        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "passwordHash");
    }

    [Fact]
    public void Create_RejectsUndefinedRoleEnumValue()
    {
        var act = () => User.Create("user@example.com", ValidHash, (UserRole)999);
        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "role");
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Picker)]
    [InlineData(UserRole.Dispatcher)]
    public void Create_AcceptsEachDefinedRole(UserRole role)
    {
        var user = User.Create("user@example.com", ValidHash, role);
        user.Role.Should().Be(role);
    }

    [Fact]
    public void UpdatePassword_ChangesHashAndRaisesEvent()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents(); // drop the Create event so we observe Update in isolation
        const string newHash = "$argon2id$v=19$m=65536,t=4,p=4$bmV3$bmV3aGFzaA";

        user.UpdatePassword(newHash);

        user.PasswordHash.Should().Be(newHash);
        user.UpdatedAt.Should().NotBeNull();
        user.DomainEvents.Should().ContainSingle(e => e is UserPasswordChangedEvent);
        var evt = (UserPasswordChangedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void UpdatePassword_RejectsEmptyHash(string? hash)
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        var act = () => user.UpdatePassword(hash!);
        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "newPasswordHash");
    }

    [Fact]
    public void SetRole_ChangesRoleAndRaisesEvent()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.SetRole(UserRole.Picker);

        user.Role.Should().Be(UserRole.Picker);
        user.UpdatedAt.Should().NotBeNull();
        user.DomainEvents.Should().ContainSingle(e => e is UserRoleChangedEvent);
        var evt = (UserRoleChangedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
        evt.FromRole.Should().Be(UserRole.Owner);
        evt.ToRole.Should().Be(UserRole.Picker);
    }

    [Fact]
    public void SetRole_NoOpWhenSameRole_NoEventAndNoUpdatedAtBump()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();
        var before = user.UpdatedAt;

        user.SetRole(UserRole.Owner);

        user.Role.Should().Be(UserRole.Owner);
        user.UpdatedAt.Should().Be(before); // unchanged
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetRole_RejectsUndefinedRoleEnumValue()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        var act = () => user.SetRole((UserRole)999);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "newRole");
    }

    [Fact]
    public void Deactivate_FlipsIsActiveAndBumpsUpdatedAt()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deactivate_IsIdempotent_DoesNotBumpUpdatedAtAgain()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.Deactivate();
        var firstUpdate = user.UpdatedAt;

        user.Deactivate(); // again

        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().Be(firstUpdate); // unchanged
    }

    [Fact]
    public void RecordLogin_SetsLastLoginAt()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.LastLoginAt.Should().BeNull();
        var before = DateTime.UtcNow.AddSeconds(-1);

        user.RecordLogin();

        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt.Should().BeAfter(before);
    }

    [Fact]
    public void RecordLogin_DoesNotRaiseDomainEvent()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.RecordLogin();

        user.DomainEvents.Should().BeEmpty();
    }
}
