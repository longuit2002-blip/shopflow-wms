using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure.SignalR;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Authorization;

/// <summary>
/// Sprint-10.5 U3 — reflection-based coverage that pins the
/// <see cref="PermissionKeys.HubConnect"/> policy on <see cref="TenantHub"/>.
/// Sprint-10 attached <c>[Authorize(Policy = ...)]</c> to 33 controller
/// actions across the four business modules but left the hub on the bare
/// <c>[Authorize]</c> shape. This test closes that gap and bisects future
/// regressions (a teammate dropping the attribute swap) without paying the
/// cost of U4's Docker-backed integration suite.
/// </summary>
public sealed class TenantHubAuthorizePolicyTests
{
    [Fact]
    public void TenantHub_CarriesAuthorizeAttribute_WithHubConnectPolicy()
    {
        var attribute = typeof(TenantHub).GetCustomAttribute<AuthorizeAttribute>(inherit: false);

        attribute
            .Should()
            .NotBeNull("TenantHub must carry a class-level [Authorize] attribute (Sprint-10.5 U3)");
        attribute!
            .Policy.Should()
            .Be(
                PermissionKeys.HubConnect,
                "TenantHub must be gated by the 'hub.connect' policy per KTD4 — the policy "
                    + "inherits RequireAuthenticatedUser() from AddShopFlowPermissionPolicies "
                    + "and adds RequireClaim(\"perm\", \"hub.connect\")"
            );
    }

    [Fact]
    public void TenantHub_DoesNotCarryBareAuthorize()
    {
        var bareAuthorize = typeof(TenantHub)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Any(a => string.IsNullOrEmpty(a.Policy));

        bareAuthorize
            .Should()
            .BeFalse(
                "Sprint-10.5 U3 swapped the bare [Authorize] for the explicit "
                    + "[Authorize(Policy = PermissionKeys.HubConnect)]; any reappearance "
                    + "of the unpolicied form would reintroduce the Sprint-10 gap"
            );
    }

    [Fact]
    public void TenantHub_StillCarries_SkipTenantRoutingAttribute()
    {
        var skipAttr = typeof(TenantHub).GetCustomAttribute<SkipTenantRoutingAttribute>(
            inherit: false
        );

        skipAttr
            .Should()
            .NotBeNull(
                "TenantHub.OnConnectedAsync reads tenant_slug from the JWT claim inside "
                    + "TenantBindingHubFilter — TenantRoutingMiddleware must not reject the "
                    + "negotiation request as missing tenant context (Sprint-7 KTD2)"
            );
    }

    [Fact]
    public void HubConnect_IsRegistered_InPermissionKeysCatalog()
    {
        // Catalog-integrity guard. If HubConnect ever drops out of PermissionKeys.All,
        // AddShopFlowPermissionPolicies would silently skip the policy registration and
        // GetPolicyAsync("hub.connect") would return null — every TenantHub connect
        // would then return 500/403 depending on framework-version behaviour.
        PermissionKeys
            .All.Should()
            .Contain(
                PermissionKeys.HubConnect,
                "HubConnect must remain in the reflection-built All catalog so "
                    + "AddShopFlowPermissionPolicies registers a matching policy"
            );
    }
}
