using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Authorization;

/// <summary>
/// Sprint-9 U1 — pin the PermissionKeys catalog shape that downstream
/// policy registration (U7) + JWT issuance (U6) + role-permissions seed
/// (U12) all consume.
/// </summary>
public sealed class PermissionKeysTests
{
    [Fact]
    public void All_EnumeratesPublicStaticConstStrings()
    {
        PermissionKeys.All.Should().NotBeEmpty();
        PermissionKeys.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_ContainsCanonicalAuthAdminKeys()
    {
        PermissionKeys.All.Should().Contain(new[]
        {
            PermissionKeys.AuthAdminUsersList,
            PermissionKeys.AuthAdminUsersCreate,
            PermissionKeys.AuthAdminUsersUpdateRole,
            PermissionKeys.AuthAdminUsersResetPassword,
            PermissionKeys.AuthAdminUsersDeactivate,
            PermissionKeys.AuthAdminLockoutUnlock,
            PermissionKeys.AuthAdminMfaReset,
            PermissionKeys.AuthAdminRolePermissionsRead,
            PermissionKeys.AuthAdminRolePermissionsUpdate,
        });
    }

    [Fact]
    public void All_ContainsInventoryAndOutboundAndHubKeys()
    {
        PermissionKeys.All.Should().Contain(new[]
        {
            PermissionKeys.InventoryRead,
            PermissionKeys.InventoryAdjust,
            PermissionKeys.InventorySkusWrite,
            PermissionKeys.OutboundOrdersRead,
            PermissionKeys.OutboundOrdersWrite,
            PermissionKeys.HubConnect,
        });
    }

    [Fact]
    public void OwnerCritical_IsNonEmpty()
    {
        PermissionKeys.OwnerCritical.Should().NotBeEmpty();
    }

    [Fact]
    public void OwnerCritical_IsSubsetOfAll()
    {
        PermissionKeys.OwnerCritical.Should().BeSubsetOf(PermissionKeys.All);
    }

    [Fact]
    public void OwnerCritical_ContainsAuthAdminUsersAndRolePermissionsKeys()
    {
        // KTD13: stripping these would lock the tenant out of self-administration.
        PermissionKeys.OwnerCritical.Should().Contain(new[]
        {
            PermissionKeys.AuthAdminUsersCreate,
            PermissionKeys.AuthAdminUsersUpdateRole,
            PermissionKeys.AuthAdminRolePermissionsUpdate,
            PermissionKeys.AuthAdminLockoutUnlock,
        });
    }

    [Fact]
    public void All_KeysFollowDottedNamespaceConvention()
    {
        // Permission keys use dotted segments (module.surface.action) so the
        // JWT 'perm' claim values are URL/JSON safe and grep-friendly.
        foreach (var key in PermissionKeys.All)
        {
            key.Should().NotBeNullOrWhiteSpace();
            key.Should().MatchRegex("^[a-z][a-z0-9.-]*$",
                "permission keys are lowercase, dot/hyphen-separated tokens");
            key.Should().Contain(".", "every key carries at least one dotted segment");
        }
    }
}
