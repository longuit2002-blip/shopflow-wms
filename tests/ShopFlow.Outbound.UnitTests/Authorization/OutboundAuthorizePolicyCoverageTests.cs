using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using ShopFlow.Outbound.Api.Controllers;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Outbound.UnitTests.Authorization;

/// <summary>
/// Sprint-10 U2 — per-module reflection-based coverage test that pins the
/// canonical action→policy mapping for every routed HTTP action on the
/// Outbound module's <see cref="OrdersController"/>. KTD1 keeps the
/// assertion table close to the controller it guards; KTD2 references
/// <see cref="PermissionKeys"/> constants directly so a typo in either the
/// controller attribute or the catalog fails this test rather than slipping
/// through the build.
/// </summary>
/// <remarks>
/// <para>AE1 (class-level <c>[Authorize]</c> dropped) is covered by
/// <see cref="OrdersController_DoesNotCarryClassLevelAuthorize"/>.
/// <c>[AllowAnonymous]</c> is treated as an opt-out from policy presence;
/// no covered action carries it today.</para>
///
/// <para>KTD6 orphan-key handling — <see cref="PermissionKeys.OutboundOrdersCancel"/>
/// is catalogued in <see cref="PermissionKeys.All"/> but no
/// <c>CancelOrder</c> action exists on <see cref="OrdersController"/>
/// today. This test does NOT enforce "every catalogued key has at least
/// one application"; it only documents the orphan via
/// <see cref="OutboundOrdersCancel_RemainsCataloguedButUnapplied"/> so a
/// sloppy edit that removes the key surfaces here.</para>
/// </remarks>
public sealed class OutboundAuthorizePolicyCoverageTests
{
    [Fact]
    public void OrdersController_CreateAsync_RequiresOutboundOrdersWritePolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.CreateAsync),
            PermissionKeys.OutboundOrdersWrite
        );
    }

    [Fact]
    public void OrdersController_GetByIdAsync_RequiresOutboundOrdersReadPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.GetByIdAsync),
            PermissionKeys.OutboundOrdersRead
        );
    }

    [Fact]
    public void OrdersController_ListAsync_RequiresOutboundOrdersReadPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.ListAsync),
            PermissionKeys.OutboundOrdersRead
        );
    }

    [Fact]
    public void OrdersController_GetKpisAsync_RequiresOutboundOrdersReadPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.GetKpisAsync),
            PermissionKeys.OutboundOrdersRead
        );
    }

    [Fact]
    public void OrdersController_GetTransitionsAsync_RequiresOutboundOrdersReadPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.GetTransitionsAsync),
            PermissionKeys.OutboundOrdersRead
        );
    }

    [Fact]
    public void OrdersController_SeedAsync_RequiresOutboundOrdersWritePolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.SeedAsync),
            PermissionKeys.OutboundOrdersWrite
        );
    }

    [Fact]
    public void OrdersController_ConfirmPickAsync_RequiresOutboundOrdersPickConfirmPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.ConfirmPickAsync),
            PermissionKeys.OutboundOrdersPickConfirm
        );
    }

    [Fact]
    public void OrdersController_MarkPickFailedAsync_RequiresOutboundOrdersPickConfirmPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.MarkPickFailedAsync),
            PermissionKeys.OutboundOrdersPickConfirm
        );
    }

    [Fact]
    public void OrdersController_ConfirmPackAsync_RequiresOutboundOrdersPackConfirmPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.ConfirmPackAsync),
            PermissionKeys.OutboundOrdersPackConfirm
        );
    }

    [Fact]
    public void OrdersController_ConfirmShipAsync_RequiresOutboundOrdersShipConfirmPolicy()
    {
        AssertActionPolicy<OrdersController>(
            nameof(OrdersController.ConfirmShipAsync),
            PermissionKeys.OutboundOrdersShipConfirm
        );
    }

    [Fact]
    public void OrdersController_DoesNotCarryClassLevelAuthorize()
    {
        AssertNoClassLevelAuthorize<OrdersController>();
    }

    [Fact]
    public void EveryRoutedAction_OnCoveredControllers_HasAuthorizePolicyFromCatalog()
    {
        var controllers = new[] { typeof(OrdersController) };

        foreach (var controller in controllers)
        {
            foreach (var method in GetRoutedActions(controller))
            {
                if (method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null)
                {
                    continue;
                }

                var authorize = method
                    .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                    .ToList();

                authorize
                    .Should()
                    .ContainSingle(
                        $"{controller.Name}.{method.Name} must carry exactly one [Authorize(Policy=...)] attribute"
                    );

                var policy = authorize[0].Policy;
                policy
                    .Should()
                    .NotBeNullOrWhiteSpace(
                        $"{controller.Name}.{method.Name} [Authorize] must set Policy to a PermissionKeys value"
                    );
                PermissionKeys
                    .All.Should()
                    .Contain(
                        policy!,
                        $"{controller.Name}.{method.Name} policy '{policy}' must be a registered PermissionKeys entry"
                    );
            }
        }
    }

    [Fact]
    public void EveryPolicyAssertedHere_IsInPermissionKeysCatalog()
    {
        // Mirrors the per-action mapping in this test class. If a new action
        // joins OrdersController, both the per-action fact above and this
        // catalog list must grow together.
        var asserted = new[]
        {
            PermissionKeys.OutboundOrdersRead,
            PermissionKeys.OutboundOrdersWrite,
            PermissionKeys.OutboundOrdersPickConfirm,
            PermissionKeys.OutboundOrdersPackConfirm,
            PermissionKeys.OutboundOrdersShipConfirm,
        };

        asserted.Should().BeSubsetOf(PermissionKeys.All);
    }

    [Fact]
    public void OutboundOrdersCancel_RemainsCataloguedButUnapplied()
    {
        // KTD6 — outbound.orders.cancel is catalogued for the eventual
        // CancelOrder action but no such action exists on OrdersController
        // today. This assertion documents the orphan: if a sloppy edit
        // removes the key from PermissionKeys.All the failure surfaces here
        // before silently breaking the future Cancel action's policy gate.
        PermissionKeys.All.Should().Contain(PermissionKeys.OutboundOrdersCancel);
    }

    // Illustrative negative path: uncomment to confirm the enumerative test
    // catches an action that is missing [Authorize(Policy=...)]. Kept Skip'd
    // so the suite stays green in CI.
    [Fact(Skip = "Illustrative — uncomment locally to verify the negative path")]
    public void Illustrative_ActionWithoutAuthorizePolicy_FailsCoverage()
    {
        // var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetByIdAsync))!;
        // method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
        //     .Should().BeEmpty("synthetic — proves enumerative test would fail");
    }

    private static void AssertActionPolicy<TController>(string methodName, string expectedPolicy)
    {
        var method = typeof(TController).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );

        method
            .Should()
            .NotBeNull(
                $"{typeof(TController).Name}.{methodName} must exist as a public instance method"
            );

        var authorize = method!.GetCustomAttributes<AuthorizeAttribute>(inherit: false).ToList();

        authorize
            .Should()
            .ContainSingle(
                $"{typeof(TController).Name}.{methodName} must carry exactly one [Authorize] attribute"
            );
        authorize[0]
            .Policy.Should()
            .Be(
                expectedPolicy,
                $"{typeof(TController).Name}.{methodName} must be gated by '{expectedPolicy}'"
            );
    }

    private static void AssertNoClassLevelAuthorize<TController>()
    {
        var attr = typeof(TController).GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        attr.Should()
            .BeNull(
                $"{typeof(TController).Name} must drop class-level [Authorize] in favour of per-action policies (AE1)"
            );
    }

    private static IEnumerable<MethodInfo> GetRoutedActions(Type controller)
    {
        return controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Any());
    }
}
