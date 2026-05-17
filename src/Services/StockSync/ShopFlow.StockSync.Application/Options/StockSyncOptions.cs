namespace ShopFlow.StockSync.Application.Options;

/// <summary>
/// Configuration surface for the StockSync engine (Sprint-5 plan U3/U4).
/// Bound from configuration section <see cref="SectionName"/>.
/// Additional circuit-breaker fields land in U5 against the same options
/// class so the dispatcher pipeline reads from one place.
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

    /// <summary>
    /// Token-bucket defaults applied per <c>(tenant, channel)</c> pair
    /// by <c>TenantChannelBucketRegistry</c> (Sprint-5 plan U4, R6).
    /// Per-tenant override surface arrives Phase-3 — Sprint-5 ships one
    /// shared template.
    /// </summary>
    public TokenBucketSettings TokenBucket { get; init; } = new();

    /// <summary>
    /// Per-tenant bounded-channel capacities (Sprint-5 plan U4, R10).
    /// High lane stays small (flash-sale traffic is bursty but short);
    /// normal lane carries baseline mirror traffic.
    /// </summary>
    public QueueCapacity QueueCapacity { get; init; } = new();

    /// <summary>
    /// Token-bucket parameters for the per-<c>(tenant, channel)</c>
    /// rate limiter.
    /// </summary>
    public sealed class TokenBucketSettings
    {
        /// <summary>Tokens replenished per second.</summary>
        public int Sustain { get; init; } = 10;

        /// <summary>Bucket capacity (peak burst).</summary>
        public int Burst { get; init; } = 50;

        /// <summary>Maximum pending acquires before rejection.</summary>
        public int QueueLimit { get; init; } = 100;
    }

    /// <summary>
    /// Bounded-channel capacities for the high + normal priority lanes
    /// of <c>PerTenantQueue</c>.
    /// </summary>
    public sealed class QueueCapacity
    {
        /// <summary>High-priority lane capacity (flash-sale traffic).</summary>
        public int HighCap { get; init; } = 1_000;

        /// <summary>Normal-priority lane capacity (baseline traffic).</summary>
        public int NormalCap { get; init; } = 10_000;
    }
}
