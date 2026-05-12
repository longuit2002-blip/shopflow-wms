namespace ShopFlow.Inventory.IntegrationTests.ScaleGate;

/// <summary>
/// Per-tenant fairness math for the W3 noisy-neighbor gate. The headline
/// metric is <c>min(p99) / max(p99)</c> across tenants — when this dips
/// below 0.85 (the plan's R4 floor), one tenant is starving the others.
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

    public static double FairnessFloor(IReadOnlyDictionary<string, double> p99ByTenant)
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
