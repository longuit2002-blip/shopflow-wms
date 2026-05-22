using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using ShopFlow.Inventory.Api.Controllers;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Inventory.UnitTests.Authorization;

/// <summary>
/// Sprint-10 U1 — per-module reflection-based coverage test that pins the
/// canonical action→policy mapping for every routed HTTP action on the
/// Inventory module's controllers. KTD1 keeps the assertion table close to
/// the controllers it guards; KTD2 references <see cref="PermissionKeys"/>
/// constants directly so a typo in either the controller attribute or the
/// catalog fails this test rather than slipping through the build.
/// </summary>
/// <remarks>
/// AE1 (class-level <c>[Authorize]</c> dropped) is covered by the
/// <c>NoClassLevelAuthorize</c> facts. <c>[AllowAnonymous]</c> is treated as
/// an opt-out from policy presence; no covered action carries it today.
/// </remarks>
public sealed class InventoryAuthorizePolicyCoverageTests
{
    [Fact]
    public void InventoryController_Summary_RequiresInventoryReadPolicy()
    {
        AssertActionPolicy<InventoryController>(
            nameof(InventoryController.Summary),
            PermissionKeys.InventoryRead
        );
    }

    [Fact]
    public void SkusController_List_RequiresInventoryReadPolicy()
    {
        AssertActionPolicy<SkusController>(
            nameof(SkusController.List),
            PermissionKeys.InventoryRead
        );
    }

    [Fact]
    public void SkusController_Ledger_RequiresInventoryReadPolicy()
    {
        AssertActionPolicy<SkusController>(
            nameof(SkusController.Ledger),
            PermissionKeys.InventoryRead
        );
    }

    [Fact]
    public void SkusController_Update_RequiresInventorySkusWritePolicy()
    {
        AssertActionPolicy<SkusController>(
            nameof(SkusController.Update),
            PermissionKeys.InventorySkusWrite
        );
    }

    [Fact]
    public void SkusController_Create_RequiresInventorySkusWritePolicy()
    {
        AssertActionPolicy<SkusController>(
            nameof(SkusController.Create),
            PermissionKeys.InventorySkusWrite
        );
    }

    [Fact]
    public void SkusController_SetThreshold_RequiresInventorySkusThresholdWritePolicy()
    {
        AssertActionPolicy<SkusController>(
            nameof(SkusController.SetThreshold),
            PermissionKeys.InventorySkusThresholdWrite
        );
    }

    [Fact]
    public void SkusController_SetFlashSale_RequiresInventorySkusFlashSaleWritePolicy()
    {
        AssertActionPolicy<SkusController>(
            nameof(SkusController.SetFlashSale),
            PermissionKeys.InventorySkusFlashSaleWrite
        );
    }

    [Fact]
    public void AdjustmentsController_Adjust_RequiresInventoryAdjustPolicy()
    {
        AssertActionPolicy<AdjustmentsController>(
            nameof(AdjustmentsController.Adjust),
            PermissionKeys.InventoryAdjust
        );
    }

    [Fact]
    public void InventoryController_DoesNotCarryClassLevelAuthorize()
    {
        AssertNoClassLevelAuthorize<InventoryController>();
    }

    [Fact]
    public void SkusController_DoesNotCarryClassLevelAuthorize()
    {
        AssertNoClassLevelAuthorize<SkusController>();
    }

    [Fact]
    public void AdjustmentsController_DoesNotCarryClassLevelAuthorize()
    {
        AssertNoClassLevelAuthorize<AdjustmentsController>();
    }

    [Fact]
    public void EveryRoutedAction_OnCoveredControllers_HasAuthorizePolicyFromCatalog()
    {
        var controllers = new[]
        {
            typeof(InventoryController),
            typeof(SkusController),
            typeof(AdjustmentsController),
        };

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
        // joins one of these controllers, both the per-action fact above and
        // this catalog list must grow together.
        var asserted = new[]
        {
            PermissionKeys.InventoryRead,
            PermissionKeys.InventorySkusWrite,
            PermissionKeys.InventorySkusThresholdWrite,
            PermissionKeys.InventorySkusFlashSaleWrite,
            PermissionKeys.InventoryAdjust,
        };

        asserted.Should().BeSubsetOf(PermissionKeys.All);
    }

    // Illustrative negative path: uncomment to confirm the enumerative test
    // catches an action that is missing [Authorize(Policy=...)]. Kept Skip'd
    // so the suite stays green in CI.
    [Fact(Skip = "Illustrative — uncomment locally to verify the negative path")]
    public void Illustrative_ActionWithoutAuthorizePolicy_FailsCoverage()
    {
        // var method = typeof(InventoryController).GetMethod(nameof(InventoryController.Summary))!;
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
