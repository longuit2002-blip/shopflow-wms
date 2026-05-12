using Npgsql;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.IntegrationTests.ScaleGate;
using Xunit.Abstractions;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// The headline W3 scale-gate test (Sprint-1-redux U5, plan R4):
/// <c>5 tenants × 1,000 concurrent reservations each, against
/// total_qty=1000 per tenant</c>. Each tenant produces exactly 1,000
/// successes + 0 OVERSOLD; cross-tenant isolation holds (tenant A's
/// reservations are 0% in tenant B's DB); per-tenant fairness floor
/// (<c>min(p99) / max(p99)</c>) ≥ 0.85.
/// </summary>
/// <remarks>
/// <para>Tagged <c>Category=Load</c> (in addition to Integration) so the
/// per-PR CI lane skips it — the suite runs nightly + on-demand.
/// Wall-time on a developer laptop with Docker Desktop is typically 30-60s.
/// On dev machines without Docker (the U6 sign-off scenario) the test is
/// skipped via the InventoryTenantFixture's container startup failure.</para>
///
/// <para>Per the plan's Risks &amp; Dependencies table: if the fairness
/// floor dips below 0.85, tune PgBouncer's per-DB pool size upward and
/// document in <c>docs/solutions/</c>. Production hardware re-validates
/// at Phase-2.</para>
/// </remarks>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Load")]
public sealed class MultiTenantScaleGateTests
{
    private const string ScaleSku = "SKU-SCALE";
    private const int TenantsInScaleGate = 5;
    private const int ReservationsPerTenant = 1000;
    private const double FairnessFloor = 0.85;

    private readonly InventoryTenantFixture _fx;
    private readonly ITestOutputHelper _output;

    public MultiTenantScaleGateTests(InventoryTenantFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _output = output;
    }

    [Fact]
    public async Task FiveTenants_OneThousandConcurrentEach_FairnessFloorHolds()
    {
        var tenants = new List<ProvisionedTenant>(TenantsInScaleGate);
        for (var i = 1; i <= TenantsInScaleGate; i++)
        {
            var t = await _fx.ProvisionTenantAsync($"scale-{i}");
            await _fx.SeedStockAsync(t, ScaleSku, available: ReservationsPerTenant);
            tenants.Add(t);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ttl = TimeSpan.FromMinutes(5);

        var runs = await Task.WhenAll(
            tenants.Select(t =>
                TenantHarness.RunAsync(
                    t,
                    ScaleSku,
                    callCount: ReservationsPerTenant,
                    quantity: Quantity.From(1),
                    ttl: ttl,
                    ct: cts.Token
                )
            )
        );

        foreach (var run in runs)
        {
            _output.WriteLine(
                $"tenant={run.TenantSlug} success={run.SuccessCount} oversold={run.OversoldCount} "
                    + $"other={run.OtherFailureCount} p99={run.SuccessLatencyP99:F1}ms duration={run.TotalDuration.TotalMilliseconds:F0}ms"
            );
        }

        // Per-tenant correctness — every reservation succeeds when demand matches supply.
        foreach (var run in runs)
        {
            run.SuccessCount.Should().Be(ReservationsPerTenant);
            run.OversoldCount.Should().Be(0);
            run.OtherFailureCount.Should().Be(0);
        }

        // Cross-tenant isolation — every tenant DB has exactly 1000 ledger rows
        // and the SKUs are scoped to that tenant.
        for (var i = 0; i < tenants.Count; i++)
        {
            var count = await CountReservationsAsync(tenants[i]);
            count.Should().Be(ReservationsPerTenant);
        }

        // Fairness floor — min(p99) / max(p99) ≥ 0.85.
        var p99ByTenant = runs.ToDictionary(r => r.TenantSlug, r => r.SuccessLatencyP99);
        var fairness = FairnessCalculator.FairnessFloor(p99ByTenant);
        _output.WriteLine($"fairness floor = {fairness:F3}");
        fairness
            .Should()
            .BeGreaterThanOrEqualTo(
                FairnessFloor,
                because: "the W3 noisy-neighbor gate requires min(p99)/max(p99) ≥ 0.85"
            );
    }

    [Fact]
    public async Task OneTenant_OverDemand_OneSuccessOnly_RestOversold()
    {
        var tenant = await _fx.ProvisionTenantAsync("scale-tight");
        await _fx.SeedStockAsync(tenant, ScaleSku, available: 1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var run = await TenantHarness.RunAsync(
            tenant,
            ScaleSku,
            callCount: 100,
            quantity: Quantity.From(1),
            ttl: TimeSpan.FromMinutes(5),
            ct: cts.Token
        );

        run.SuccessCount.Should().Be(1);
        run.OversoldCount.Should().Be(99);
        run.OtherFailureCount.Should().Be(0);
    }

    private static async Task<int> CountReservationsAsync(ProvisionedTenant tenant)
    {
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM reservations_ledger";
        var scalar = (long)(await cmd.ExecuteScalarAsync())!;
        return (int)scalar;
    }
}
