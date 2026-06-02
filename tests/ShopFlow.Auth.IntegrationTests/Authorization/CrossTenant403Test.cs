using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.TestSupport;
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
/// <para>Finish-line U5: now runs against <see cref="MultiTenantAuthFixture"/>
/// (catalog + tenant-a/tenant-b DBs migrated + role_permissions seeded) and is
/// gated by <see cref="ProofFactAttribute"/> — <c>task proofs</c> locally, or
/// automatically in CI.</para>
/// </summary>
[Collection(MultiTenantAuthCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CrossTenant403Test
{
    private readonly MultiTenantAuthFixture _fixture;

    public CrossTenant403Test(MultiTenantAuthFixture fixture)
    {
        _fixture = fixture;
    }

    // Finish-line U5 — the multi-tenant Auth WAF fixture (catalog + tenant-a +
    // tenant-b DBs migrated + role_permissions seeded) makes the request route
    // for real. UseAuthorization runs BEFORE UseTenantRouting, so the full
    // 24-key perm[] passes authorization; the ONLY remaining rejection path is
    // the tenant boundary, which fires as a 403 conflict (header tenant-b ≠ JWT
    // tenant-a). Proves perm[] completeness never rescues a cross-tenant request.
    [ProofFact]
    public async Task CrossTenantJwt_WithFullPermSet_NeverReturns200()
    {
        // Arrange — build a JWT for tenant-A carrying every key in
        // PermissionKeys.All. The token authentication is valid; the only
        // thing the request lacks is tenant alignment.
        var jwt = _fixture.JwtBuilder.Build(
            tenantSlug: _fixture.TenantA.Slug,
            userId: _fixture.TenantA.OwnerUserId,
            includeKeys: PermissionKeys.All
        );

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // Force the resolved tenant to tenant-B via the live routing header.
        // TenantRoutingMiddleware honors header > JWT > subdomain and rejects a
        // 2+ source conflict; header (tenant-b) ≠ JWT (tenant-a) → 403.
        client.DefaultRequestHeaders.Add("X-ShopFlow-Tenant", _fixture.TenantB.Slug);

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
