namespace ShopFlow.StockSync.Application.Options;

/// <summary>
/// Configuration surface for the StockSync engine (Sprint-5 plan U3).
/// Bound from configuration section <see cref="SectionName"/>.
/// Additional token-bucket + circuit-breaker fields land in U4/U5 against
/// the same options class so the dispatcher pipeline reads from one place.
/// </summary>
public sealed class StockSyncOptions
{
    public const string SectionName = "StockSync";

    /// <summary>
    /// Flush cadence for <c>CoalesceFlushService</c>. Default 500ms keeps
    /// the engine's emit-rate ≥ 100x lower than raw Inventory mutation
    /// rate during flash-sale bursts (R3 / R4).
    /// </summary>
    public int CoalesceWindowMs { get; init; } = 500;

    /// <summary>
    /// Channels to fan StockLevelChanged out to. Sprint-5 default ships
    /// just <c>shopee</c>; Phase-3 reads from the Channel module's
    /// <c>channels</c> table per-tenant.
    /// </summary>
    public string[] ActiveChannels { get; init; } = new[] { "shopee" };
}
