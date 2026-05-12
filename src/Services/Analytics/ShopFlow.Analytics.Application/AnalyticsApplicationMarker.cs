namespace ShopFlow.Analytics.Application;

/// <summary>
/// Placeholder so the Analytics.Application csproj has at least one type
/// (plan U9). Analytics has no Domain project per AGENTS.md §11.76 — it
/// is the documented read-side-only exception. Real query handlers
/// against the read projections land in Phase-2.
/// </summary>
public static class AnalyticsApplicationMarker
{
    public const string ModuleName = "Analytics";
}
