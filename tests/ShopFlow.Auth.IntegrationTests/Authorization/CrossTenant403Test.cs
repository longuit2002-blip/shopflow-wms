using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Auth.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 adv-6 — single cross-tenant boundary proof.
///
/// <para>The 33 single-tenant 403 tests prove enforcement on
/// <c>perm[]</c> set membership but say nothing about the tenant
/// boundary. This test pins the SECOND defense-in-depth layer: a
/// tenant-A JWT carrying the FULL 24-key <c>perm[]</c> set, presented
/// against a tenant-B-resolved request, must NEVER return 200. The
/// expected outcome is 401 (JwtBearer rejects because the
/// <c>tenant_slug</c> claim doesn't match the resolved tenant) OR
/// 403 (per-action policy fires because the per-request DbContext
/// binding sees no matching role row), but NEVER 200.</para>
///
/// <para>Implementation note (KTD6 carry-forward): the file path the
/// plan specified is in Auth.IntegrationTests; the original plan body
/// referenced <c>POST /api/v1/inventory/adjustments</c>, which would
/// require booting Inventory.Api from this project — but doing so
/// collides with Auth.Api's own <c>public partial class Program</c>
/// declaration (both live in the global namespace). The semantically
/// identical proof is exercised against
/// <c>GET /api/auth/admin/users</c> with full <c>perm[]</c> — the
/// tenant-mismatch path is the same regardless of which controller
/// hosts the gated endpoint.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture. CI's nightly +
/// per-PR Docker-backed job removes the Skip.</para>
/// </summary>
[Collection(AuthAdminAuthorizationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CrossTenant403Test
{
    private readonly AuthAdminAuthorizationFixture _fixture;

    public CrossTenant403Test(AuthAdminAuthorizationFixture fixture)
    {
        _fixture = fixture;
    }

    // Finish-line U2 → U5. The 3 boot-blocking bugs are fixed in U2
    // (ForwardedHeaders guard config + TenantTemplate '{db}' token in
    // AuthAdminAuthorizationFixture, and the production JwtTokenIssuer
    // Singleton→Scoped DI lifetime bug). The WAF now boots and serves, but
    // this cross-tenant assertion returns 500 (not 401/403) because the
    // fixture provisions only ONE tenant DB and no migrated catalog — the
    // X-Tenant: tenant-b request resolves to an unprovisioned tenant. Full
    // green needs the multi-tenant Auth WAF fixture (catalog + tenant-a/
    // tenant-b DBs migrated + role_permissions seeded), which finish-line U5
    // builds for the AuthCrossTenant harness; this test rejoins Category=Proof
    // there.
    [Fact(
        Skip = "finish-line U5: needs the multi-tenant Auth WAF fixture (catalog + 2 tenant DBs migrated + role_permissions seeded). U2 fixed the 3 boot-blocking bugs; full cross-tenant green lands with U5."
    )]
    public async Task CrossTenantJwt_WithFullPermSet_NeverReturns200()
    {
        // Arrange — build a JWT for tenant-A carrying every key in
        // PermissionKeys.All (24 entries). The token authentication is
        // valid; the only thing the request lacks is tenant alignment.
        var tenantASlug = "tenant-a";
        var jwt = _fixture.JwtBuilder.Build(
            tenantSlug: tenantASlug,
            userId: Guid.NewGuid(),
            includeKeys: PermissionKeys.All.ToArray()
        );

        // CI tier provisions tenant-A + tenant-B as two distinct
        // Postgres databases on the shared container; the resolved
        // request host below MUST route to tenant-B (subdomain in the
        // Host header or X-Tenant header — whichever the live
        // TenantRoutingMiddleware honors).
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // Override the resolved tenant via the documented routing
        // signal. TenantRoutingMiddleware (Sprint-1-redux) honors
        // header > JWT > subdomain in that priority; setting the
        // X-Tenant header forces tenant-B regardless of the JWT's
        // tenant_slug=tenant-a claim. Conflict resolution should
        // surface as 401 (audit row + reject) per the existing
        // 2+ source conflict rule.
        client.DefaultRequestHeaders.Add("X-Tenant", "tenant-b");

        // Act — call any policy-gated endpoint. The full perm[]
        // guarantees the policy gate cannot reject; the only available
        // rejection path is the tenant boundary.
        var response = await client.GetAsync("/api/auth/admin/users");

        // Assert — never 200. Either 401 (tenant_slug claim ≠ resolved
        // tenant) or 403 (per-tenant DbContext binding sees no
        // role_permissions row matching the claim). NEVER 200.
        response
            .StatusCode.Should()
            .BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
