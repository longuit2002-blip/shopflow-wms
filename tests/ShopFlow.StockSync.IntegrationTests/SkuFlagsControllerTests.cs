namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U7 — <c>SkuFlagsController</c> HTTP shape. Deferred to
/// U8 because the StockSync.Api <c>Program.cs</c> ships in U1 as a stub
/// that does NOT wire <c>AddStockSyncModule</c> or
/// <c>UseTenantRouting()</c>: without the routing middleware the
/// controller's <see cref="ShopFlow.SharedKernel.Application.IRequestContext"/>
/// dependency cannot be populated, so <c>WebApplicationFactory&lt;Program&gt;</c>
/// can't drive an end-to-end PUT.
/// </summary>
/// <remarks>
/// <para>U8 composes the module (<c>AddShopFlowDefaults → AddControlPlane
/// → AddStockSyncModule → UseTenantRouting</c>) and at that point this
/// suite turns into a real WebApplicationFactory-based integration test:
/// PUT /api/skus/{sku}/flag with X-ShopFlow-Tenant header → 204 +
/// repository state advances, T1 vs T2 isolation, missing body 400,
/// missing tenant 400.</para>
///
/// <para>Until then the placeholder below documents the planned test
/// surface; the cache contract is locked by
/// <c>SkuFlagCacheTests</c> and the DB-backed inner repo by
/// <c>SkuFlagRepositoryIntegrationTests</c>, so U7's verification floor
/// is fully met.</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SkuFlagsControllerTests
{
    [Fact(
        Skip = "U8 wires AddStockSyncModule + UseTenantRouting; "
            + "controller HTTP shape covered then. See plan U7 → U8 hand-off."
    )]
    public void PlaceholderUntilU8Composition()
    {
        // U8 will replace this with WebApplicationFactory<Program> tests:
        //   • PUT /api/skus/SKU-X/flag { "isFlashSale": true } with
        //     X-ShopFlow-Tenant header → 204 + the SkuFlag row exists
        //     in the tenant DB with is_flash_sale=true.
        //   • Duplicate PUT (same body) → 204, row unchanged
        //     (aggregate's idempotent setter).
        //   • PUT with empty body → 400 with code "stocksync.body.required".
        //   • PUT without X-ShopFlow-Tenant → 400 via middleware (the
        //     routing middleware rejects before the controller runs).
        //   • Cross-tenant isolation: T1 PUT, T2 GET state — separate DBs.
    }
}
