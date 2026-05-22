using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using ShopFlow.Inbound.Api.Controllers;
using ShopFlow.SharedKernel.Authorization;

namespace ShopFlow.Inbound.UnitTests.Authorization;

/// <summary>
/// Sprint-10 U3 — reflection-based attribute coverage pin for the Inbound
/// HTTP surface (PurchaseOrdersController). Every public action method
/// carrying an <see cref="HttpMethodAttribute"/> MUST also carry exactly
/// one <see cref="AuthorizeAttribute"/> whose <see cref="AuthorizeAttribute.Policy"/>
/// equals one of the catalogued <see cref="PermissionKeys"/> constants
/// (KTD1 + KTD2 — references the constants directly so a rename surfaces
/// as a compile error here).
/// </summary>
/// <remarks>
/// <para>Per KTD3, the class-level <see cref="AuthorizeAttribute"/> on
/// <see cref="PurchaseOrdersController"/> stays absent — gating is per-action
/// only. The <c>NoClassLevelAuthorize</c> structural fact pins that posture
/// (true before AND after the Sprint-10 edit; the test prevents future drift).</para>
///
/// <para>Inbound canonical mapping (KTD8): Create / Open / Cancel → InboundPosWrite;
/// GetById / ListOpen → InboundPosRead; ReceiveLine → InboundReceiveConfirm.
/// `outbound.orders.cancel` style of catalogued-but-unapplied keys does NOT
/// apply to Inbound — every Inbound key in the catalog attaches to ≥ 1 action.</para>
/// </remarks>
public sealed class InboundAuthorizePolicyCoverageTests
{
    [Fact]
    public void PurchaseOrdersController_CreateAsync_RequiresInboundPosWritePolicy()
    {
        AssertActionRequiresPolicy(
            nameof(PurchaseOrdersController.CreateAsync),
            PermissionKeys.InboundPosWrite
        );
    }

    [Fact]
    public void PurchaseOrdersController_GetByIdAsync_RequiresInboundPosReadPolicy()
    {
        AssertActionRequiresPolicy(
            nameof(PurchaseOrdersController.GetByIdAsync),
            PermissionKeys.InboundPosRead
        );
    }

    [Fact]
    public void PurchaseOrdersController_ListOpenAsync_RequiresInboundPosReadPolicy()
    {
        AssertActionRequiresPolicy(
            nameof(PurchaseOrdersController.ListOpenAsync),
            PermissionKeys.InboundPosRead
        );
    }

    [Fact]
    public void PurchaseOrdersController_OpenAsync_RequiresInboundPosWritePolicy()
    {
        AssertActionRequiresPolicy(
            nameof(PurchaseOrdersController.OpenAsync),
            PermissionKeys.InboundPosWrite
        );
    }

    [Fact]
    public void PurchaseOrdersController_CancelAsync_RequiresInboundPosWritePolicy()
    {
        AssertActionRequiresPolicy(
            nameof(PurchaseOrdersController.CancelAsync),
            PermissionKeys.InboundPosWrite
        );
    }

    [Fact]
    public void PurchaseOrdersController_ReceiveLineAsync_RequiresInboundReceiveConfirmPolicy()
    {
        AssertActionRequiresPolicy(
            nameof(PurchaseOrdersController.ReceiveLineAsync),
            PermissionKeys.InboundReceiveConfirm
        );
    }

    [Fact]
    public void PurchaseOrdersController_HasNoClassLevelAuthorizeAttribute()
    {
        // Sprint-10 KTD3 — gating is strictly per-action. A class-level
        // [Authorize] would silently widen the policy surface and hide
        // per-action drift. None exists today; this test pins that.
        var classAttr = typeof(PurchaseOrdersController).GetCustomAttribute<AuthorizeAttribute>(
            inherit: false
        );

        classAttr
            .Should()
            .BeNull("PurchaseOrdersController gates per-action only (Sprint-10 KTD3)");
    }

    [Fact]
    public void PurchaseOrdersController_EveryHttpActionCarriesAuthorizeAttribute()
    {
        // Enumerative guard (covers AE3 — adding a new HTTP action without
        // [Authorize(Policy=...)] fails this test, not a per-action fact).
        var actions = GetHttpActions(typeof(PurchaseOrdersController)).ToList();

        actions
            .Should()
            .NotBeEmpty("PurchaseOrdersController has at least one [HttpVerb]-decorated action");

        foreach (var action in actions)
        {
            var authorize = action.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
            authorize
                .Should()
                .NotBeNull(
                    $"action {action.Name} must carry [Authorize(Policy=...)] (Sprint-10 R1/R8)"
                );
            authorize!
                .Policy.Should()
                .NotBeNullOrWhiteSpace(
                    $"action {action.Name} [Authorize] must name a Policy (not a bare [Authorize])"
                );
        }
    }

    [Fact]
    public void PurchaseOrdersController_EveryAuthorizePolicyIsCataloguedInPermissionKeys()
    {
        // Catalog integrity — a typo in a Policy literal (e.g. "inbound.po.write"
        // vs "inbound.pos.write") would slip past per-action facts that bind
        // to constants by accident; this fact rereads the wire-side string and
        // verifies it round-trips through PermissionKeys.All.
        var actions = GetHttpActions(typeof(PurchaseOrdersController)).ToList();

        foreach (var action in actions)
        {
            var authorize = action.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
            authorize.Should().NotBeNull();
            PermissionKeys
                .All.Should()
                .Contain(
                    authorize!.Policy!,
                    $"action {action.Name} Policy '{authorize.Policy}' must be in PermissionKeys.All"
                );
        }
    }

    // -----------------------------------------------------------------
    // Negative-shape illustration (KTD1) — kept as a Skip'd fact so the
    // intent of the enumerative guard above is grep-discoverable. If a
    // future action ships without [Authorize(Policy=...)], the live
    // EveryHttpActionCarriesAuthorizeAttribute fact above will fail.
    // -----------------------------------------------------------------
    [Fact(
        Skip = "Illustrative-only: documents the failure shape EveryHttpActionCarriesAuthorizeAttribute pins."
    )]
    public void NegativePathShape_ActionWithoutAuthorize_WouldFailEnumerativeGuard()
    {
        // pretend a future HttpDelete action shipped without [Authorize]:
        //   [HttpDelete("{id:guid}")]
        //   public Task<IActionResult> DeleteAsync(Guid id) => ...
        // GetHttpActions would return it, its GetCustomAttribute<AuthorizeAttribute>
        // would be null, and EveryHttpActionCarriesAuthorizeAttribute would
        // fail with a precise per-action message.
    }

    private static IEnumerable<MethodInfo> GetHttpActions(Type controllerType) =>
        controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(inherit: false).OfType<HttpMethodAttribute>().Any());

    private static void AssertActionRequiresPolicy(string methodName, string expectedPolicy)
    {
        var action = typeof(PurchaseOrdersController).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );

        action
            .Should()
            .NotBeNull(
                $"PurchaseOrdersController.{methodName} must exist as a public action method"
            );

        action!
            .GetCustomAttributes(inherit: false)
            .OfType<HttpMethodAttribute>()
            .Should()
            .NotBeEmpty($"{methodName} must be decorated with an [HttpVerb] attribute");

        var authorize = action.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        authorize
            .Should()
            .NotBeNull(
                $"{methodName} must carry [Authorize(Policy = PermissionKeys.{expectedPolicy})]"
            );
        authorize!
            .Policy.Should()
            .Be(expectedPolicy, $"{methodName} must gate on the canonical policy {expectedPolicy}");
    }
}
