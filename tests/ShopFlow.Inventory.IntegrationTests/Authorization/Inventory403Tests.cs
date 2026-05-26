using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Inventory.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — 403 wire-shape pinning for the 8 Inventory actions
/// Sprint-10 U1 attached per-action <c>[Authorize(Policy = PermissionKeys.X)]</c>
/// to. Each fact submits a JWT whose <c>perm[]</c> set <em>omits</em> the
/// action's required key (and includes the other 23 of 24 keys); the
/// kernel <c>JwtBearer</c> validator accepts the token's authentication,
/// the per-action policy engine rejects on authorization, ASP.NET Core
/// returns 403. Pins runtime enforcement on top of Sprint-10's
/// compile-time reflection tests.
///
/// <para>Endpoint mapping (verified against the live controllers at
/// <c>src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/</c>):</para>
/// <list type="bullet">
///   <item><c>GET /api/v1/inventory/summary</c> → omit <c>InventoryRead</c></item>
///   <item><c>GET /api/v1/inventory/skus</c> (list) → omit <c>InventoryRead</c></item>
///   <item><c>GET /api/v1/inventory/skus/{sku}/ledger</c> → omit <c>InventoryRead</c></item>
///   <item><c>PUT /api/v1/inventory/skus/{sku}</c> → omit <c>InventorySkusWrite</c></item>
///   <item><c>POST /api/v1/inventory/skus</c> → omit <c>InventorySkusWrite</c></item>
///   <item><c>PUT /api/v1/inventory/skus/{sku}/threshold</c> → omit <c>InventorySkusThresholdWrite</c></item>
///   <item><c>PUT /api/v1/inventory/skus/{sku}/flash-sale</c> → omit <c>InventorySkusFlashSaleWrite</c></item>
///   <item><c>POST /api/v1/inventory/adjustments</c> → omit <c>InventoryAdjust</c></item>
/// </list>
///
/// <para>Skip-marked locally per Sprint-1+ posture. CI removes the Skip
/// via the existing nightly + per-PR Docker-backed job.</para>
/// </summary>
[Collection(InventoryAuthorizationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class Inventory403Tests
{
    private const string SkipReason =
        "Sprint-10.5 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    private readonly InventoryAuthorizationFixture _fixture;

    public Inventory403Tests(InventoryAuthorizationFixture fixture)
    {
        _fixture = fixture;
    }

    // ── GET /api/v1/inventory/summary ────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Summary_RejectsJwtMissingInventoryRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventoryRead);
        var response = await client.GetAsync("/api/v1/inventory/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/v1/inventory/skus ───────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task ListSkus_RejectsJwtMissingInventoryRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventoryRead);
        var response = await client.GetAsync("/api/v1/inventory/skus");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/v1/inventory/skus/{sku}/ledger ──────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Ledger_RejectsJwtMissingInventoryRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventoryRead);
        var response = await client.GetAsync("/api/v1/inventory/skus/SKU-A/ledger");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/v1/inventory/skus/{sku} ─────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task UpdateSku_RejectsJwtMissingInventorySkusWrite_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventorySkusWrite);
        var body = new { name = "Test SKU", isFlashSale = false };
        var response = await client.PutAsJsonAsync("/api/v1/inventory/skus/SKU-A", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/v1/inventory/skus ──────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task CreateSku_RejectsJwtMissingInventorySkusWrite_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventorySkusWrite);
        var body = new { sku = "SKU-NEW", initialAvailable = 0 };
        var response = await client.PostAsJsonAsync("/api/v1/inventory/skus", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/v1/inventory/skus/{sku}/threshold ───────────────────

    [Fact(Skip = SkipReason)]
    public async Task SetThreshold_RejectsJwtMissingInventorySkusThresholdWrite_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventorySkusThresholdWrite);
        var body = new { threshold = 10 };
        var response = await client.PutAsJsonAsync("/api/v1/inventory/skus/SKU-A/threshold", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/v1/inventory/skus/{sku}/flash-sale ──────────────────

    [Fact(Skip = SkipReason)]
    public async Task SetFlashSale_RejectsJwtMissingInventorySkusFlashSaleWrite_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventorySkusFlashSaleWrite);
        var body = new { active = true };
        var response = await client.PutAsJsonAsync("/api/v1/inventory/skus/SKU-A/flash-sale", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/v1/inventory/adjustments ───────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Adjust_RejectsJwtMissingInventoryAdjust_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InventoryAdjust);
        var body = new
        {
            sku = "SKU-A",
            delta = 1,
            reason = "test",
            note = (string?)null,
        };
        var response = await client.PostAsJsonAsync("/api/v1/inventory/adjustments", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Build an HttpClient whose Authorization header carries a JWT with
    /// every key in <see cref="PermissionKeys.All"/> EXCEPT
    /// <paramref name="omittedKey"/>. Failing solely on the omitted key
    /// proves the per-action policy fires for that key specifically.
    /// </summary>
    private HttpClient BuildClientNarrowedFor(string omittedKey)
    {
        var includeKeys = PermissionKeys
            .All.Where(k => !string.Equals(k, omittedKey, StringComparison.Ordinal))
            .ToArray();
        var jwt = _fixture.JwtBuilder.Build(
            tenantSlug: InventoryAuthorizationFixture.TenantSlug,
            userId: Guid.NewGuid(),
            includeKeys: includeKeys
        );
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }
}
