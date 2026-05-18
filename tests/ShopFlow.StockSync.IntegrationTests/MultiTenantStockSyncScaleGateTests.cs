namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U9 — multi-tenant scale-gate tests (R8 noisy-neighbor +
/// R9 breaker recovery). Tagged <c>Category=Load</c> so the default
/// <c>dotnet test</c> filter excludes them; CI nightly runs
/// <c>--filter Category=Load</c>.
/// </summary>
/// <remarks>
/// <para><strong>Sprint-5 ships these as <c>Skip</c>'d slots</strong>,
/// mirroring Sprint-4 U9's <c>MultiTenantWebhookScaleGateTests</c> precedent.
/// The body relies on:</para>
/// <list type="bullet">
///   <item><description>5-tenant provisioning + warm-up via <see cref="StockSyncTenantFixture"/></description></item>
///   <item><description><see cref="Drivers.TenantBurstDriver"/> (Sprint-5 U9 ships the helper) for 2k events/s direct outbox-row insertion</description></item>
///   <item><description><see cref="FairnessCalculator"/> (Sprint-5 U9 ships the helper) for per-tenant push-count fairness ratio</description></item>
///   <item><description>Real Shopee mock running alongside StockSync.Api so the bucket + breaker have a real downstream — the U9 happy-path test uses <see cref="Drivers.FakeChannelAdapterFactory"/> instead because the multi-tenant Aspire boot is the U9 harness gap.</description></item>
/// </list>
///
/// <para>The Sprint-4 precedent was: ship the slots with descriptive
/// <c>Skip</c> messages, document the wall-time measurement deferral in
/// the sign-off doc, close the harness in a follow-up sprint. Sprint-5
/// follows the same posture — the production primitives all ship in
/// U3-U8; the gate is a measurement artifact.</para>
///
/// <para>What CI captures today:</para>
/// <list type="bullet">
///   <item><description>U3 unit tests prove coalescing collapses redundant
///   updates to one entry per <c>(tenant, sku, channel)</c></description></item>
///   <item><description>U4 unit tests prove the token bucket rate-limits
///   correctly and the queue routes flash-sale to high-priority</description></item>
///   <item><description>U5 unit tests prove the breaker opens after
///   <c>MinimumThroughput</c> failures and recovers via half-open probe</description></item>
///   <item><description>U6 integration tests prove the Shopee adapter
///   round-trips through the mock + handles chaos 5xx</description></item>
///   <item><description>U8 composition test proves the full host wires
///   the dispatcher + hosted services correctly</description></item>
/// </list>
///
/// <para>The scale gate composes these into a wall-time measurement:
/// "under 5 tenants with one bursting, do tenants B-E meet the p99 SLO?"
/// That measurement requires real-marketplace-mock + multi-tenant Aspire
/// boot — out of Sprint-5's harness scope. Sign-off doc records the
/// deferral as a Phase-2 follow-up, same as Sprint-4 U9 → Sprint-4.5 U5.</para>
/// </remarks>
[Trait("Category", "Load")]
public sealed class MultiTenantStockSyncScaleGateTests
{
    private const string SprintHarnessFollowUpSkip =
        "Sprint-5.5 follow-up — multi-tenant Aspire boot + real Shopee mock harness "
        + "deferred per Sprint-4 U9 precedent. Production primitives proven by U3/U4/U5/U6/U8 "
        + "unit + integration tests; wall-time measurement composes them in a follow-up.";

    /// <summary>
    /// Sprint-5 plan R8 — 5 tenants × tenant A burst 2k stock-changes/s × 5 min;
    /// tenants B-E maintain p99 end-to-end &lt; 30s; per-tenant fairness
    /// floor (min push / max push, excluding A) ≥ 0.85.
    /// </summary>
    [Fact(Skip = SprintHarnessFollowUpSkip)]
    public Task NoisyNeighborBurst_TenantA2kRps_5min_BurnsAlone_AndBcdeMeetSlo()
    {
        // Harness shape (follow-up):
        //   1. Provision 5 tenants via StockSyncTenantFixture; warm-up 30s at 10/s each
        //   2. Boot StockSync.Api once (multi-tenant; dispatcher enumerates all 5
        //      via ITenantCatalog.GetReadyTenantsAsync)
        //   3. Boot Shopee mock alongside; configure StockSync.Api to point at mock URL
        //   4. Measurement phase (5 min wall-clock):
        //      - Tenant A: 2000/s on SKU-FLASH (is_flash_sale=true)
        //        via Drivers.TenantBurstDriver parallel-batch inserts
        //      - Tenants B-E: 10/s each on diverse SKUs (is_flash_sale=false)
        //   5. Collect push_log rows; compute per-tenant p99 (pushed_at - observed_at)
        //   6. Compute FairnessCalculator.Compute(perTenantCount excluding A)
        //   7. Assert: p99(B..E) < 30s, fairness >= 0.85
        //   8. NpgsqlConnection.ClearAllPools() in TearDown (Sprint-3-redux U8 precedent)
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sprint-5 plan R9 — chaos-toggle on tenant A trips breaker; B unaffected;
    /// after cooldown A recovers without cross-tenant blast.
    /// </summary>
    [Fact(Skip = SprintHarnessFollowUpSkip)]
    public Task BreakerRecovery_ChaosToggleOnTenantA_TripsThenRecovers_BUnaffected()
    {
        // Harness shape (follow-up):
        //   Phase 1 (t=0..10s): both tenants A+B push 50/s. All succeed.
        //   Phase 2 (t=10..30s): POST mock /__chaos {is_stock_update_chaos:true}.
        //     - Tenant A's breaker trips after MinimumThroughput=5 failures.
        //     - push_log shows stock_sync.breaker.open entries for A.
        //     - Tenant B continues at 50/s (limitation: process-wide chaos in Sprint-5 mock
        //       — B will also see failures; per-tenant chaos is Phase-3).
        //   Phase 3 (t=30..70s): chaos off; wait BreakDuration=60s.
        //   Phase 4 (t=70s+): A's breaker probes Closed; throughput resumes.
        //
        //   Assertions:
        //     - B push rate stays at 50/s (±20%) through phases 1, 3, 4
        //     - A throughput drops to ~0 in phase 2; recovers ≥ 30/s within 90s of chaos-off
        return Task.CompletedTask;
    }
}
