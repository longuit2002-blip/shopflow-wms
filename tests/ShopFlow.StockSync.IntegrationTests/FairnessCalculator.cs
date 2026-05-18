namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Per-tenant fairness math for Sprint-5 U9's noisy-neighbor + breaker-recovery
/// scale gates. Mirrors the Sprint-1-redux <c>Inventory</c>, Sprint-3-redux
/// <c>Outbound</c>, and Sprint-4.5 <c>Channel</c> implementations verbatim —
/// the fairness floor formula is universal (<c>min / max</c>), kept as a
/// per-test-project peer so each scale gate can drift independently if the
/// math ever needs to.
/// </summary>
/// <remarks>
/// Two metrics live here side-by-side because the StockSync gate measures
/// fairness on both p99 latency (noisy-neighbor) AND raw push counts
/// (breaker recovery — the breaker-tripped tenant goes to ~0 while peers
/// stay steady). The percentile helper is generic across both call sites.
/// </remarks>
internal static class FairnessCalculator
{
    public static double Percentile(IReadOnlyList<double> samplesMs, double percentile)
    {
        if (samplesMs.Count == 0)
        {
            return 0;
        }
        var sorted = samplesMs.OrderBy(v => v).ToArray();
        var rank = (percentile / 100.0) * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi)
        {
            return sorted[lo];
        }
        var weight = rank - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * weight;
    }

    public static double FairnessFloor<TKey>(IReadOnlyDictionary<TKey, double> byTenant)
        where TKey : notnull
    {
        if (byTenant.Count == 0)
        {
            return 1.0;
        }
        var max = byTenant.Values.Max();
        if (max <= 0)
        {
            return 1.0;
        }
        var min = byTenant.Values.Min();
        return min / max;
    }
}
