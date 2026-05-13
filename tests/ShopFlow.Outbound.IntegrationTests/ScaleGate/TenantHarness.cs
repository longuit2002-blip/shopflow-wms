using ShopFlow.Outbound.IntegrationTests.Fixtures;

namespace ShopFlow.Outbound.IntegrationTests.ScaleGate;

/// <summary>
/// Sprint-3-redux U8 — per-tenant timing harness for the W5 scale gate.
/// Thin facade over <see cref="LoadTestOrderGenerator.RunAsync"/>; the
/// generator already captures per-order latency in two buckets (Shipped
/// vs. Cancelled). This wrapper exists to mirror the Sprint-1-redux
/// <c>TenantHarness</c> shape — one Run-Per-Tenant API — so the test
/// reads the same way at the top level.
/// </summary>
internal static class TenantHarness
{
    public static Task<TenantRunResult> RunAsync(
        ProvisionedOutboundTenant tenant,
        int orderCount,
        int driverParallelism,
        double pickFailureRate,
        CancellationToken ct
    )
    {
        return LoadTestOrderGenerator.RunAsync(
            tenant,
            orderCount,
            driverParallelism,
            pickFailureRate,
            ct
        );
    }
}

/// <summary>
/// Per-tenant run outcome. <see cref="ShippedLatencies"/> and
/// <see cref="CancelledLatencies"/> are the per-order wall-times (POST →
/// terminal state) in ms for the two outcome buckets; the p99 derivations
/// below feed the fairness floor + the W5 5-min/60-s assertions.
/// </summary>
internal sealed record TenantRunResult(
    string TenantSlug,
    int ShippedCount,
    int CancelledCount,
    int ErrorCount,
    double[] ShippedLatencies,
    double[] CancelledLatencies,
    string[] FailureSamples,
    TimeSpan TotalDuration
)
{
    public int TotalCount => ShippedCount + CancelledCount + ErrorCount;

    /// <summary>
    /// p99 of the happy-path (Shipped) wall-times. The W5 headline target.
    /// Empty samples → 0 (treated as "perfectly fast" — the count
    /// assertions catch the actual no-orders-completed degenerate case).
    /// </summary>
    public double ShippedLatencyP99 => FairnessCalculator.Percentile(ShippedLatencies, 99);

    /// <summary>
    /// p99 of the compensation path (Cancelled) wall-times. The 5%-variant
    /// target is 60 s.
    /// </summary>
    public double CancelledLatencyP99 => FairnessCalculator.Percentile(CancelledLatencies, 99);
}
