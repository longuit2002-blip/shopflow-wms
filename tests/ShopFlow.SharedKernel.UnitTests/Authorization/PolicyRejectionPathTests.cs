using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Authorization;

/// <summary>
/// Sprint-10.5 adv-3 — bisects framework vs JWT-shape vs policy-composition
/// regressions ahead of U4's Docker-backed 33-test suite.
/// Sprint-9.5 <c>PermissionPolicyCompositionTests</c> verify policy
/// COMPOSITION (DI shape, claim requirements) but never call
/// <c>IAuthorizationService.AuthorizeAsync</c> on a deficient principal.
/// This test does — it constructs a <see cref="ClaimsPrincipal"/> directly
/// (no JWT validation, no MVC pipeline, no HTTP) and drives the in-process
/// authorization service through both the happy + rejection paths. If the
/// 33-action Docker suite ever fails, this test answers "is the framework
/// itself broken, or is the JWT / policy wiring at fault?" in &lt; 1 second.
/// </summary>
public sealed class PolicyRejectionPathTests
{
    private static (
        IAuthorizationService Service,
        IAuthorizationPolicyProvider Provider
    ) BuildHost()
    {
        var services = new ServiceCollection();
        // DefaultAuthorizationService takes ILogger<DefaultAuthorizationService> as a
        // ctor dep; AddLogging() registers the ILogger<T> open-generic resolver so the
        // ServiceProvider can build it. AddOptions() satisfies IOptions<AuthorizationOptions>
        // which AddAuthorization() expects to be registered (it is, but being explicit is
        // load-bearing-free + tracks the production composition shape).
        services.AddLogging();
        services.AddAuthorization();
        services.AddShopFlowPermissionPolicies();
        var sp = services.BuildServiceProvider();
        return (
            sp.GetRequiredService<IAuthorizationService>(),
            sp.GetRequiredService<IAuthorizationPolicyProvider>()
        );
    }

    private static ClaimsPrincipal BuildPrincipal(IEnumerable<string> permClaims)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user-id") };
        claims.AddRange(permClaims.Select(p => new Claim("perm", p)));

        // AuthenticationType MUST be non-null/non-empty so ClaimsIdentity.IsAuthenticated
        // returns true; otherwise DenyAnonymousAuthorizationRequirement short-circuits
        // before the perm-claim check and the test would conflate the two failure modes.
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task AuthorizeAsync_RejectsPrincipal_MissingOneRequiredPermClaim()
    {
        var (service, provider) = BuildHost();

        // Carry 23 of 24 perm claims (all keys EXCEPT InventoryAdjust). Then
        // attempt to authorise against the InventoryAdjust policy.
        var permsExceptInventoryAdjust = PermissionKeys
            .All.Where(k => k != PermissionKeys.InventoryAdjust)
            .ToArray();
        var principal = BuildPrincipal(permsExceptInventoryAdjust);

        var policy = await provider.GetPolicyAsync(PermissionKeys.InventoryAdjust);
        policy.Should().NotBeNull("InventoryAdjust policy must be registered");

        var result = await service.AuthorizeAsync(principal, resource: null, policy!);

        result
            .Succeeded.Should()
            .BeFalse(
                "principal carrying every perm EXCEPT 'inventory.adjust' must fail the "
                    + "InventoryAdjust policy — proves RequireClaim(\"perm\", key) is "
                    + "actually being evaluated against the principal, not silently skipped"
            );
    }

    [Fact]
    public async Task AuthorizeAsync_AcceptsPrincipal_CarryingEveryRequiredPermClaim()
    {
        var (service, provider) = BuildHost();

        // Carry all 24 perm claims. Authorise against the same InventoryAdjust policy.
        var principal = BuildPrincipal(PermissionKeys.All);

        var policy = await provider.GetPolicyAsync(PermissionKeys.InventoryAdjust);
        policy.Should().NotBeNull("InventoryAdjust policy must be registered");

        var result = await service.AuthorizeAsync(principal, resource: null, policy!);

        result
            .Succeeded.Should()
            .BeTrue(
                "principal carrying 'inventory.adjust' among its perm claims must pass "
                    + "the InventoryAdjust policy — the happy path that the 33-action "
                    + "Docker suite exercises end-to-end is reproducible in-process here"
            );
    }
}
