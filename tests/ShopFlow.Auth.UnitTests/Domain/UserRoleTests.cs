using FluentAssertions;
using ShopFlow.Auth.Domain;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Domain;

/// <summary>
/// Sprint-8 U1 — locks the <see cref="UserRole"/> contract. The enum
/// names and the DB-level CHECK constraints
/// (<c>role IN ('Owner', 'Picker', 'Dispatcher', 'Packer')</c>) shipped in
/// Sprint-8 U3's AddUsers migration + Sprint-9 U3's AddSprint9AuthSchema
/// (role_permissions mirror) + Sprint-13 U1's AddPackerRole widening must
/// agree exactly: adding a 5th role is a coordinated change (enum +
/// per-tenant migration on BOTH constraints + downstream consumers of
/// <c>UserRoleChangedEvent</c>). This test would fail loudly if someone
/// re-ordered, renamed, or extended the enum without that coordination —
/// surfacing the contract change at unit-test time instead of at
/// CHECK-constraint-violation time in CI.
/// </summary>
public sealed class UserRoleTests
{
    [Fact]
    public void HasExactlyFourMembers()
    {
        var members = Enum.GetValues<UserRole>();
        members.Should().HaveCount(4);
    }

    [Fact]
    public void MembersAreOwnerPickerDispatcherPacker()
    {
        var names = Enum.GetNames<UserRole>();
        names.Should().BeEquivalentTo(new[] { "Owner", "Picker", "Dispatcher", "Packer" });
    }

    [Theory]
    [InlineData(UserRole.Owner, "Owner")]
    [InlineData(UserRole.Picker, "Picker")]
    [InlineData(UserRole.Dispatcher, "Dispatcher")]
    [InlineData(UserRole.Packer, "Packer")]
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

    [Fact]
    public void PackerAppendsAtIndexThree()
    {
        // Sprint-13 K9 — Packer was appended at the END of the enum
        // (index 3) to preserve Owner=0/Picker=1/Dispatcher=2 binary
        // serialization ordering. Pin the index so a future re-order
        // can't silently shift values that downstream JWT claims, EF
        // string conversions, and audit-log replay paths depend on.
        ((int)UserRole.Owner)
            .Should()
            .Be(0);
        ((int)UserRole.Picker).Should().Be(1);
        ((int)UserRole.Dispatcher).Should().Be(2);
        ((int)UserRole.Packer).Should().Be(3);
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Picker)]
    [InlineData(UserRole.Dispatcher)]
    [InlineData(UserRole.Packer)]
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
