using FluentAssertions;
using ShopFlow.Auth.Domain;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Domain;

/// <summary>
/// Sprint-8 U1 — locks the <see cref="UserRole"/> contract. The enum
/// names and the DB-level CHECK constraint
/// (<c>role IN ('Owner', 'Picker', 'Dispatcher')</c>) shipped in U3's
/// AddUsers migration must agree exactly: adding a 4th role in Sprint-9+
/// is a coordinated change (enum + per-tenant migration + downstream
/// consumers of <c>UserRoleChangedEvent</c>). This test would fail loudly
/// if someone re-ordered, renamed, or extended the enum without that
/// coordination — surfacing the contract change at unit-test time
/// instead of at CHECK-constraint-violation time in CI.
/// </summary>
public sealed class UserRoleTests
{
    [Fact]
    public void HasExactlyThreeMembers()
    {
        var members = Enum.GetValues<UserRole>();
        members.Should().HaveCount(3);
    }

    [Fact]
    public void MembersAreOwnerPickerDispatcher()
    {
        var names = Enum.GetNames<UserRole>();
        names.Should().BeEquivalentTo(new[] { "Owner", "Picker", "Dispatcher" });
    }

    [Theory]
    [InlineData(UserRole.Owner, "Owner")]
    [InlineData(UserRole.Picker, "Picker")]
    [InlineData(UserRole.Dispatcher, "Dispatcher")]
    public void ToStringYieldsTheExactNameUsedInTheCheckConstraint(UserRole role, string expected)
    {
        role.ToString().Should().Be(expected);
    }

    [Fact]
    public void OwnerIsTheDefaultValue()
    {
        // First-declared member sits at 0 — and Owner is the privileged role
        // that the first-tenant-user seed (U10 seed-owner) provisions. Pin it
        // so an enum re-order can't silently demote a freshly-seeded admin.
        ((int)UserRole.Owner)
            .Should()
            .Be(0);
        default(UserRole).Should().Be(UserRole.Owner);
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Picker)]
    [InlineData(UserRole.Dispatcher)]
    public void EachShippedMemberIsDefined(UserRole role)
    {
        Enum.IsDefined(role).Should().BeTrue();
    }

    [Fact]
    public void UndefinedNumericValueIsNotDefined()
    {
        // The Create/SetRole guards on User reject undefined values via
        // Enum.IsDefined. Pin the negative case here so a future
        // [Flags]-style refactor that breaks IsDefined surfaces in U1
        // before it lets a junk role into the aggregate.
        Enum.IsDefined((UserRole)999).Should().BeFalse();
    }
}
