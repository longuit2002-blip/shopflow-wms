using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Authorization;

/// <summary>
/// Sprint-9 U7 — pin that AddShopFlowPermissionPolicies registers one
/// policy per <see cref="PermissionKeys.All"/> entry with the canonical
/// RequireAuthenticatedUser + RequireClaim("perm", &lt;key&gt;) shape.
/// </summary>
public sealed class PermissionPolicyCompositionTests
{
    private static IAuthorizationPolicyProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddShopFlowPermissionPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationPolicyProvider>();
    }

    [Fact]
    public async Task RegistersOnePolicyPerPermissionKey()
    {
        var provider = BuildProvider();

        foreach (var key in PermissionKeys.All)
        {
            var policy = await provider.GetPolicyAsync(key);
            policy.Should().NotBeNull($"AddShopFlowPermissionPolicies must register a policy named '{key}'");
        }
    }

    [Fact]
    public async Task EveryPolicy_RequiresAuthenticatedUser()
    {
        var provider = BuildProvider();
        var key = PermissionKeys.InventoryRead;

        var policy = await provider.GetPolicyAsync(key);

        policy!.Requirements.OfType<DenyAnonymousAuthorizationRequirement>()
            .Should().HaveCount(1, "RequireAuthenticatedUser must be present");
    }

    [Fact]
    public async Task EveryPolicy_RequiresMatchingPermClaim()
    {
        var provider = BuildProvider();
        var key = PermissionKeys.AuthAdminUsersCreate;

        var policy = await provider.GetPolicyAsync(key);

        var claimReq = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().SingleOrDefault();
        claimReq.Should().NotBeNull("RequireClaim must be present");
        claimReq!.ClaimType.Should().Be("perm");
        claimReq.AllowedValues.Should().Contain(key);
    }

    [Fact]
    public async Task PolicyForUnknownKey_ReturnsNull()
    {
        var provider = BuildProvider();

        var policy = await provider.GetPolicyAsync("not.a.real.permission");

        policy.Should().BeNull("unknown policy names fall through to default-deny");
    }
}
