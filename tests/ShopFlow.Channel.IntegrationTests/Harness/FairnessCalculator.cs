namespace ShopFlow.Channel.IntegrationTests.Harness;

/// <summary>
/// Per-tenant fairness math for Sprint-4.5 U5's receiver-side scale gate.
/// Mirrors the Inventory + Outbound implementations from Sprint-1-redux U5
/// and Sprint-3-redux U8 verbatim — fairness floor formula is universal
/// (<c>min(p99) / max(p99)</c>), kept as a per-test-project peer so each
/// scale gate can drift independently if the math ever needs to.
/// </summary>
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

    public static double FairnessFloor(IReadOnlyDictionary<int, double> p99ByTenant)
    {
        if (p99ByTenant.Count == 0)
        {
            return 1.0;
        }
        var max = p99ByTenant.Values.Max();
        if (max <= 0)
        {
            return 1.0;
        }
        var min = p99ByTenant.Values.Min();
        return min / max;
    }
}
