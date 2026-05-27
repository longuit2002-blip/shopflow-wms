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
        RolePermissionsSeed
            .PickerBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersRead)
            .And.Contain("outbound.orders.read");
    }

    [Fact]
    public void PickerBaseline_Contains_OutboundOrdersPickConfirm()
    {
        RolePermissionsSeed
            .PickerBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersPickConfirm)
            .And.Contain("outbound.orders.pick-confirm");
    }

    [Fact]
    public void PickerBaseline_Contains_InventoryRead()
    {
        RolePermissionsSeed
            .PickerBaseline.Should()
            .Contain(PermissionKeys.InventoryRead)
            .And.Contain("inventory.read");
    }

    [Fact]
    public void PickerBaseline_Contains_HubConnect()
    {
        RolePermissionsSeed
            .PickerBaseline.Should()
            .Contain(PermissionKeys.HubConnect)
            .And.Contain("hub.connect");
    }

    [Fact]
    public void PickerBaseline_Does_Not_Contain_Owner_Critical_Keys()
    {
        // Picker must never carry Owner-critical admin keys — those are
        // the keys KTD13 server-side guard locks on the Owner row.
        RolePermissionsSeed
            .PickerBaseline.Intersect(PermissionKeys.OwnerCritical)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PickerBaseline_Does_Not_Contain_Write_Keys()
    {
        // Picker is a read + single-action role (pick-confirm). It must
        // not carry generic write keys — those belong to Owner or to
        // post-Sprint-11 Dispatcher.
        RolePermissionsSeed
            .PickerBaseline.Should()
            .NotContain(
                new[]
                {
                    PermissionKeys.InventoryAdjust,
                    PermissionKeys.InventorySkusWrite,
                    PermissionKeys.OutboundOrdersWrite,
                    PermissionKeys.OutboundOrdersCancel,
                    PermissionKeys.InboundPosWrite,
                }
            );
    }

    [Fact]
    public void PickerBaseline_Keys_Are_All_In_PermissionKeys_All()
    {
        // Every baseline key must be a canonical PermissionKeys entry —
        // a typo here would mint a key the policy engine never
        // registers, silently producing a permission claim that never
        // gates anything.
        RolePermissionsSeed.PickerBaseline.Should().BeSubsetOf(PermissionKeys.All);
    }

    // ── Sprint-12 U1 — Dispatcher baseline ────────────────────────────

    [Fact]
    public void DispatcherBaseline_Has_Exactly_Three_Keys()
    {
        RolePermissionsSeed.DispatcherBaseline.Count.Should().Be(3);
    }

    [Fact]
    public void DispatcherBaseline_Contains_OutboundOrdersRead()
    {
        RolePermissionsSeed
            .DispatcherBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersRead)
            .And.Contain("outbound.orders.read");
    }

    [Fact]
    public void DispatcherBaseline_Contains_OutboundOrdersShipConfirm()
    {
        RolePermissionsSeed
            .DispatcherBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersShipConfirm)
            .And.Contain("outbound.orders.ship-confirm");
    }

    [Fact]
    public void DispatcherBaseline_Contains_HubConnect()
    {
        RolePermissionsSeed
            .DispatcherBaseline.Should()
            .Contain(PermissionKeys.HubConnect)
            .And.Contain("hub.connect");
    }

    [Fact]
    public void DispatcherBaseline_Does_Not_Contain_Owner_Critical_Keys()
    {
        // Dispatcher must never carry Owner-critical admin keys — the
        // KTD13 server-side guard locks those on the Owner row.
        RolePermissionsSeed
            .DispatcherBaseline.Intersect(PermissionKeys.OwnerCritical)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void DispatcherBaseline_Does_Not_Contain_OutboundOrdersPickConfirm()
    {
        // Dispatcher owns ship-confirm only. Pick-confirm is Picker's
        // transition; cross-contamination would break the role-confusion
        // proof Sprint-12 exists to land.
        RolePermissionsSeed
            .DispatcherBaseline.Should()
            .NotContain(PermissionKeys.OutboundOrdersPickConfirm);
    }

    [Fact]
    public void DispatcherBaseline_Does_Not_Contain_OutboundOrdersPackConfirm()
    {
        // Sprint-12 design — Pack stays Owner-only (no Packer fourth
        // role). Dispatcher owns ship-confirm only.
        RolePermissionsSeed
            .DispatcherBaseline.Should()
            .NotContain(PermissionKeys.OutboundOrdersPackConfirm);
    }

    [Fact]
    public void PickerBaseline_DoesNotContain_OutboundOrdersShipConfirm()
    {
        // Sprint-12 doc-review security-F1 mitigation — explicit
        // baseline-isolation guard. The canonical Picker baseline has
        // NO outbound.orders.ship-confirm. The runtime additive-only
        // contract (KTD1) preserves operator-added overlaps, but this
        // test ensures the baseline doesn't ship pre-overlapped (which
        // would silently grant carrier-cost capability to Picker).
        RolePermissionsSeed
            .PickerBaseline.Should()
            .NotContain(PermissionKeys.OutboundOrdersShipConfirm);
    }

    [Fact]
    public void DispatcherBaseline_Keys_Are_All_In_PermissionKeys_All()
    {
        // Every baseline key must be a canonical PermissionKeys entry.
        RolePermissionsSeed.DispatcherBaseline.Should().BeSubsetOf(PermissionKeys.All);
    }

    // ── Sprint-13 U1 — Packer baseline ────────────────────────────────

    [Fact]
    public void PackerBaseline_Has_Exactly_Three_Keys()
    {
        RolePermissionsSeed.PackerBaseline.Count.Should().Be(3);
    }

    [Fact]
    public void PackerBaseline_Contains_OutboundOrdersRead()
    {
        RolePermissionsSeed
            .PackerBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersRead)
            .And.Contain("outbound.orders.read");
    }

    [Fact]
    public void PackerBaseline_Contains_OutboundOrdersPackConfirm()
    {
        RolePermissionsSeed
            .PackerBaseline.Should()
            .Contain(PermissionKeys.OutboundOrdersPackConfirm)
            .And.Contain("outbound.orders.pack-confirm");
    }

    [Fact]
    public void PackerBaseline_Contains_HubConnect()
    {
        RolePermissionsSeed
            .PackerBaseline.Should()
            .Contain(PermissionKeys.HubConnect)
            .And.Contain("hub.connect");
    }

    [Fact]
    public void PackerBaseline_IsDispatcherShape_NoInventoryRead()
    {
        // Sprint-13 K5 — Packer mirrors Dispatcher's 3-key shape, NOT
        // Picker's 4-key shape. By pack time items are already pulled, so
        // Packer doesn't need inventory.read. Pin the absence so a future
        // edit that adds inventory.read (copying Picker) is caught.
        RolePermissionsSeed.PackerBaseline.Should().NotContain(PermissionKeys.InventoryRead);
    }

    [Fact]
    public void PackerBaseline_Does_Not_Contain_Owner_Critical_Keys()
    {
        // Packer must never carry Owner-critical admin keys — the KTD13
        // server-side guard locks those on the Owner row.
        RolePermissionsSeed
            .PackerBaseline.Intersect(PermissionKeys.OwnerCritical)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PackerBaseline_Does_Not_Contain_OutboundOrdersPickConfirm()
    {
        // Packer owns pack-confirm only. Pick-confirm is Picker's
        // transition; cross-contamination would break the 4-role
        // role-confusion proof Sprint-13 exists to land.
        RolePermissionsSeed
            .PackerBaseline.Should()
            .NotContain(PermissionKeys.OutboundOrdersPickConfirm);
    }

    [Fact]
    public void PackerBaseline_Does_Not_Contain_OutboundOrdersShipConfirm()
    {
        // Packer owns pack-confirm only. Ship-confirm is Dispatcher's
        // transition.
        RolePermissionsSeed
            .PackerBaseline.Should()
            .NotContain(PermissionKeys.OutboundOrdersShipConfirm);
    }

    [Fact]
    public void PackerBaseline_Keys_Are_All_In_PermissionKeys_All()
    {
        // Every baseline key must be a canonical PermissionKeys entry.
        RolePermissionsSeed.PackerBaseline.Should().BeSubsetOf(PermissionKeys.All);
    }

    // ── Sprint-13 U1 — security-F1 baseline-isolation guards ──────────
    // Sprint-13 moves pack-confirm to Packer. The other two non-Owner
    // roles must NOT ship pre-overlapped with pack-confirm — the runtime
    // additive-only contract (KTD1) preserves operator-added overlaps,
    // but the canonical baselines must start clean.

    [Fact]
    public void PickerBaseline_DoesNotContain_OutboundOrdersPackConfirm()
    {
        RolePermissionsSeed
            .PickerBaseline.Should()
            .NotContain(PermissionKeys.OutboundOrdersPackConfirm);
    }

    // Note: DispatcherBaseline_Does_Not_Contain_OutboundOrdersPackConfirm
    // already shipped in Sprint-12 U1 (above) and continues to hold —
    // it is the Dispatcher half of this Sprint-13 isolation pair.
}
