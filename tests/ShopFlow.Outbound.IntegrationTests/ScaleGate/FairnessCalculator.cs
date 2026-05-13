namespace ShopFlow.Outbound.IntegrationTests.ScaleGate;

/// <summary>
/// Per-tenant fairness math for Sprint-3-redux U8's W5 scale gate. Mirrors
/// <c>ShopFlow.Inventory.IntegrationTests.ScaleGate.FairnessCalculator</c>;
/// kept as a per-test-project peer so the two scale gates can drift
/// independently if the math ever needs to (it shouldn't — the fairness
/// floor formula is universal: <c>min(p99) / max(p99)</c>).
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
