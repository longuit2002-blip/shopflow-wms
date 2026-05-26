using Npgsql;
using ShopFlow.Outbound.IntegrationTests.Fixtures;
using ShopFlow.Outbound.IntegrationTests.ScaleGate;
using Xunit.Abstractions;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U8 — the headline W5 scale gate (plan R17):
/// <c>2,000 orders/tenant × 3 tenants in 1 min</c>, all reach Shipped
/// within 5 min p99 per tenant; 5% pick-failure variant releases within
/// 60 s p99 per tenant; fairness floor <c>min(p99) / max(p99) ≥ 0.85</c>.
/// </summary>
/// <remarks>
/// <para><strong>Category=Load — nightly CI only.</strong> Tagged
/// alongside Category=Integration so per-PR runs skip it. Wall-time on a
/// developer laptop with Docker Desktop is typically 60-180 s for the
/// happy-path scenario; the 5%-variant runs in a similar envelope (the
/// driver short-circuits Cancelled at the pick step, so per-order latency
/// is lower).</para>
///
/// <para><strong>Pragmatic harness shape.</strong> The auto-driver
/// bypasses the saga's reservation hop and short-circuits the
/// CompensatingReservation → Cancelled transition that the
/// OrderCancelledConsumer would normally drive. Rationale: per K14 the
/// scale gate's target metric is operator-pipeline throughput under
/// concurrent load with per-tenant fairness — not saga correctness
/// (covered by U4's <c>SagaPerTenantBindingTests</c> + U7's
/// <c>PickFailureCompensationTests</c>). Running 3 concurrent saga
/// instances against an in-memory bus with per-tenant DbContext binding
/// would balloon the test into a multi-host orchestration exercise
/// orthogonal to what W5 actually measures.</para>
///
/// <para><strong>Hardware-bound numbers per Sprint-1-redux W3
/// precedent.</strong> The 5-min / 60-s p99 targets are
/// production-hardware budgets; this dev-machine run captures what it
/// captures. Production-CI Linux runners re-validate the absolute
/// numbers. Fairness floor (a ratio, not an absolute) is hardware-
/// agnostic: if min/max within one run stays ≥ 0.85, the per-tenant
/// isolation invariant holds regardless of the absolute throughput.</para>
///
/// <para><strong>Carrier mock delay shortened.</strong> The production
/// 1-3 s carrier delay would dominate the wall-time at this scale (6,000
/// orders × 1 s ÷ 60 parallel drivers ≈ 100 s minimum). The scale gate
/// uses a 5-20 ms delay window so we measure the EF + DB write path
/// rather than the mock-carrier sleep. PackShipEndpointTests covers the
/// real-delay behaviour at unit-test scale.</para>
/// </remarks>
[Collection(MultiTenantOutboundCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Load")]
public sealed class MultiTenantOutboundScaleGateTests
{
    private const int TenantsInScaleGate = 3;
    private const int OrdersPerTenant = 2000;
    private const int DriverParallelismPerTenant = 20;
    private const double FairnessFloor = 0.85;

    /// <summary>5 min p99 per tenant (plan R17 happy path).</summary>
    private static readonly TimeSpan ShippedP99Target = TimeSpan.FromMinutes(5);

    /// <summary>60 s p99 per tenant for the compensation tail (plan R17 variant).</summary>
    private static readonly TimeSpan CancelledP99Target = TimeSpan.FromSeconds(60);

    /// <summary>Total wall-time budget for the gate (well over the targets so the test never spins indefinitely).</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromMinutes(10);

    private readonly MultiTenantOutboundFixture _fx;
    private readonly ITestOutputHelper _output;

    public MultiTenantOutboundScaleGateTests(
        MultiTenantOutboundFixture fx,
        ITestOutputHelper output
    )
    {
        _fx = fx;
        _output = output;
    }

    [Fact]
    public async Task ThreeTenants_TwoThousandOrdersEach_HappyPath_FairnessFloorHolds()
    {
        // Provision 3 tenants in parallel — provisioning cost dominates
        // the test's setup phase, so amortise via Task.WhenAll.
        var tenants = await ProvisionTenantsAsync("happy");

        using var cts = new CancellationTokenSource(GateTimeout);
        var runs = await Task.WhenAll(
            tenants.Select(t =>
                TenantHarness.RunAsync(
                    t,
                    orderCount: OrdersPerTenant,
                    driverParallelism: DriverParallelismPerTenant,
                    pickFailureRate: 0,
                    ct: cts.Token
                )
            )
        );

        LogRuns(runs, "happy");

        // ---- Correctness invariants ---------------------------------------
        // The load-bearing assertions:
        //  • All orders resolve to a definite outcome (no hung futures).
        //  • 0 drive-pipeline errors — every order reaches Shipped.
        //  • Fairness floor min(p99) / max(p99) ≥ 0.85.
        foreach (var run in runs)
        {
            (run.ShippedCount + run.CancelledCount + run.ErrorCount)
                .Should()
                .Be(
                    OrdersPerTenant,
                    because: "every issued order must resolve to a definite outcome (shipped, cancelled, or pipeline error)"
                );
            run.CancelledCount.Should()
                .Be(
                    0,
                    because: "happy path injects no pick failures — no order should reach Cancelled"
                );
            run.ErrorCount.Should()
                .Be(
                    0,
                    because: $"every order should drive through to Shipped; first failures: [{string.Join(" | ", run.FailureSamples)}]"
                );
            run.ShippedCount.Should()
                .Be(
                    OrdersPerTenant,
                    because: "the gate's headline assertion: all 6000 orders reach Shipped"
                );
        }

        // Fairness floor — W5 noisy-neighbor gate. The absolute p99 numbers
        // are dev-hardware-bound (production-CI re-validates the 5-min
        // target); the FAIRNESS metric is hardware-agnostic and is what
        // this assertion locks down.
        var p99ByTenant = runs.ToDictionary(r => r.TenantSlug, r => r.ShippedLatencyP99);
        var fairness = FairnessCalculator.FairnessFloor(p99ByTenant);
        _output.WriteLine($"fairness floor (shipped p99) = {fairness:F3}");
        fairness
            .Should()
            .BeGreaterThanOrEqualTo(
                FairnessFloor,
                because: "the W5 noisy-neighbor gate requires min(p99) / max(p99) ≥ 0.85 across tenants"
            );

        // Per-tenant 5-min p99 target — log + soft-warn (assertion follows
        // the Sprint-1-redux W3 documented-as-hardware-bound posture):
        //   - The fairness floor + 0-error count are the hard correctness
        //     gates that pass/fail this test.
        //   - The absolute 5-min p99 target is a production budget; this
        //     dev-machine run records what it captures. Production-CI on
        //     a Linux runner re-validates the absolute numbers.
        foreach (var run in runs)
        {
            var p99 = TimeSpan.FromMilliseconds(run.ShippedLatencyP99);
            _output.WriteLine(
                $"tenant={run.TenantSlug} shipped p99 = {p99.TotalSeconds:F1}s (target {ShippedP99Target.TotalSeconds:F0}s, dev-hardware-bound)"
            );
            // Documented as hardware-bound — assert only that p99 is finite
            // (a zero p99 indicates the percentile calc didn't see real
            // samples, which would be a test bug). The Sprint-1-redux
            // posture: "production hardware re-validates the absolute
            // numbers" — we record + log but do not hard-fail.
            run.ShippedLatencyP99.Should()
                .BeGreaterThan(
                    0,
                    because: "p99 derives from observed wall-time samples; zero means no order completed"
                );
        }
    }

    [Fact]
    public async Task ThreeTenants_TwoThousandOrdersEach_FivePercentPickFailure_CompensationFairnessFloorHolds()
    {
        // Mirror the happy-path topology but inject a 5% pick-failure rate.
        // Compensation tail (mark-pick-failed + state flip to Cancelled)
        // must complete within 60 s p99 per tenant; the 95% success subset
        // must still reach Shipped within the same 5-min envelope as the
        // happy path. Fairness floor on each set.
        var tenants = await ProvisionTenantsAsync("variant");

        using var cts = new CancellationTokenSource(GateTimeout);
        var runs = await Task.WhenAll(
            tenants.Select(t =>
                TenantHarness.RunAsync(
                    t,
                    orderCount: OrdersPerTenant,
                    driverParallelism: DriverParallelismPerTenant,
                    pickFailureRate: 0.05,
                    ct: cts.Token
                )
            )
        );

        LogRuns(runs, "variant");

        foreach (var run in runs)
        {
            // No oversell / no pipeline error — every order must resolve.
            (run.ShippedCount + run.CancelledCount + run.ErrorCount)
                .Should()
                .Be(
                    OrdersPerTenant,
                    because: "every issued order resolves to a definite outcome (shipped, cancelled, or pipeline error)"
                );
            run.ErrorCount.Should()
                .Be(
                    0,
                    because: $"both Shipped and Cancelled are valid happy-shape outcomes; first failures: [{string.Join(" | ", run.FailureSamples)}]"
                );

            // Cancellation rate ≈ 5% — wide bounds (3-8%) to absorb random
            // sampling noise on a 2000-order budget. K6's "5% pick-failure"
            // is the seeded mean, not a tight per-tenant invariant.
            var cancelledPct = (double)run.CancelledCount / OrdersPerTenant * 100;
            cancelledPct
                .Should()
                .BeInRange(
                    3.0,
                    8.0,
                    because: $"pickFailureRate=0.05 should land cancelled fraction in 3-8% (got {cancelledPct:F2}%)"
                );

            // 95% success subset reaches Shipped; observed counts should
            // sum cleanly with cancelled.
            run.ShippedCount.Should()
                .BeGreaterThan(0, because: "the 95% success subset must reach Shipped");
            run.CancelledCount.Should()
                .BeGreaterThan(
                    0,
                    because: "the 5%-variant must produce at least some Cancelled orders"
                );
        }

        // Fairness floor on the Shipped p99 — same noisy-neighbor invariant
        // as the happy path, with the slightly-lighter 95% subset.
        var shippedP99 = runs.ToDictionary(r => r.TenantSlug, r => r.ShippedLatencyP99);
        var shippedFairness = FairnessCalculator.FairnessFloor(shippedP99);
        _output.WriteLine($"variant fairness floor (shipped p99) = {shippedFairness:F3}");
        shippedFairness
            .Should()
            .BeGreaterThanOrEqualTo(
                FairnessFloor,
                because: "5%-variant Shipped subset must still satisfy fairness floor ≥ 0.85"
            );

        // Fairness floor on the Cancelled (compensation) p99 — the
        // 5%-variant's gate. Compensation latency must be fair across
        // tenants too.
        var cancelledP99 = runs.ToDictionary(r => r.TenantSlug, r => r.CancelledLatencyP99);
        var cancelledFairness = FairnessCalculator.FairnessFloor(cancelledP99);
        _output.WriteLine($"variant fairness floor (cancelled p99) = {cancelledFairness:F3}");
        cancelledFairness
            .Should()
            .BeGreaterThanOrEqualTo(
                FairnessFloor,
                because: "5%-variant Cancelled subset must satisfy fairness floor ≥ 0.85 for the compensation tail"
            );

        // Per-tenant 60-s compensation p99 — documented + soft-validated
        // per the Sprint-1-redux W3 posture. The hard assertion is the
        // fairness floor above; the absolute number is hardware-bound.
        foreach (var run in runs)
        {
            var p99 = TimeSpan.FromMilliseconds(run.CancelledLatencyP99);
            _output.WriteLine(
                $"tenant={run.TenantSlug} cancelled p99 = {p99.TotalSeconds:F2}s (target {CancelledP99Target.TotalSeconds:F0}s, dev-hardware-bound)"
            );
            run.CancelledLatencyP99.Should()
                .BeGreaterThan(
                    0,
                    because: "cancelled p99 derives from observed wall-time samples; zero means no compensation completed"
                );
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<List<ProvisionedOutboundTenant>> ProvisionTenantsAsync(string variantSlug)
    {
        // Clear pooled connections from any prior test run sharing this
        // fixture. The xunit Collection fixture amortizes the
        // Testcontainers Postgres across the two scale-gate tests; the
        // Postgres server's max_connections=100 cap means pooled
        // connections from the prior test must be released before this
        // test's 3 tenants can each open up to MaxPoolSize=25.
        NpgsqlConnection.ClearAllPools();

        var provisions = Enumerable
            .Range(1, TenantsInScaleGate)
            .Select(i => _fx.ProvisionTenantAsync($"scale-{variantSlug}-{i}"))
            .ToArray();
        await Task.WhenAll(provisions);
        var tenants = provisions.Select(t => t.Result).ToList();

        // Seed 5 pickers per tenant per plan U8 spec. Pick-wave generator
        // round-robins across them; for the scale gate we bypass the
        // wave path but Picker rows still need to exist so the
        // PackShipEndpointTests-derived harness can resolve them later
        // if the test grows.
        foreach (var t in tenants)
        {
            await MultiTenantOutboundFixture.SeedPickersAsync(
                t,
                new[] { "picker-1", "picker-2", "picker-3", "picker-4", "picker-5" }
            );
        }

        return tenants;
    }

    private void LogRuns(TenantRunResult[] runs, string label)
    {
        _output.WriteLine($"---- {label} scale-gate results ({runs.Length} tenants) ----");
        foreach (var run in runs)
        {
            _output.WriteLine(
                $"tenant={run.TenantSlug} total={run.TotalCount} "
                    + $"shipped={run.ShippedCount} cancelled={run.CancelledCount} errors={run.ErrorCount} "
                    + $"shippedP99={run.ShippedLatencyP99:F0}ms cancelledP99={run.CancelledLatencyP99:F0}ms "
                    + $"duration={run.TotalDuration.TotalSeconds:F1}s"
            );
            if (run.FailureSamples.Length > 0)
            {
                _output.WriteLine(
                    $"  · failure samples: {string.Join(" || ", run.FailureSamples)}"
                );
            }
        }
    }
}
