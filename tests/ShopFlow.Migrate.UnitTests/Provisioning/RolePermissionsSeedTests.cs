using FluentAssertions;
using ShopFlow.Migrate.Provisioning;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Migrate.UnitTests.Provisioning;

/// <summary>
/// Sprint-11 U1 — pins the canonical Picker 4-key baseline shape on
/// <see cref="RolePermissionsSeed.PickerBaseline"/>. The seed body
/// itself runs against real Postgres in
/// <c>ShopFlow.Migrate.IntegrationTests</c>; these tests guard the
/// constant list against silent reordering / accidental key drift.
/// </summary>
public class RolePermissionsSeedTests
{
    [Fact]
    public void PickerBaseline_Has_Exactly_Four_Keys()
    {
        RolePermissionsSeed.PickerBaseline.Count.Should().Be(4);
    }

    [Fact]
    public void PickerBaseline_Contains_OutboundOrdersRead()
    {
        RolePermissionsSeed.PickerBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersRead)
            .And.Contain("outbound.orders.read");
    }

    [Fact]
    public void PickerBaseline_Contains_OutboundOrdersPickConfirm()
    {
        RolePermissionsSeed.PickerBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersPickConfirm)
            .And.Contain("outbound.orders.pick-confirm");
    }

    [Fact]
    public void PickerBaseline_Contains_InventoryRead()
    {
        RolePermissionsSeed.PickerBaseline.Should()
            .Contain(PermissionKeys.InventoryRead)
            .And.Contain("inventory.read");
    }

    [Fact]
    public void PickerBaseline_Contains_HubConnect()
    {
        RolePermissionsSeed.PickerBaseline.Should()
            .Contain(PermissionKeys.HubConnect)
            .And.Contain("hub.connect");
    }

    [Fact]
    public void PickerBaseline_Does_Not_Contain_Owner_Critical_Keys()
    {
        // Picker must never carry Owner-critical admin keys — those are
        // the keys KTD13 server-side guard locks on the Owner row.
        RolePermissionsSeed.PickerBaseline
            .Intersect(PermissionKeys.OwnerCritical)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PickerBaseline_Does_Not_Contain_Write_Keys()
    {
        // Picker is a read + single-action role (pick-confirm). It must
        // not carry generic write keys — those belong to Owner or to
        // post-Sprint-11 Dispatcher.
        RolePermissionsSeed.PickerBaseline.Should().NotContain(new[]
        {
            PermissionKeys.InventoryAdjust,
            PermissionKeys.InventorySkusWrite,
            PermissionKeys.OutboundOrdersWrite,
            PermissionKeys.OutboundOrdersCancel,
            PermissionKeys.InboundPosWrite,
        });
    }

    [Fact]
    public void PickerBaseline_Keys_Are_All_In_PermissionKeys_All()
    {
        // Every baseline key must be a canonical PermissionKeys entry —
        // a typo here would mint a key the policy engine never
        // registers, silently producing a permission claim that never
        // gates anything.
        RolePermissionsSeed.PickerBaseline.Should()
            .BeSubsetOf(PermissionKeys.All);
    }
}
