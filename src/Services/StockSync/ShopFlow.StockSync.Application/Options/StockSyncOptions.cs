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

    /// <summary>
    /// Polly v8 circuit-breaker parameters applied per
    /// <c>(tenant, channel)</c> by <c>TenantChannelBreakerRegistry</c>
    /// (Sprint-5 plan U5, R7). Defaults match the plan's brainstorm
    /// numbers: trip on 5 consecutive failures inside a 30s sample,
    /// cool down for 60s, then probe.
    /// </summary>
    public BreakerSettings Breaker { get; init; } = new();

    /// <summary>
    /// Sprint-5 plan U8 — when <c>true</c>, the Api exposes
    /// <c>GET /api/sync/state</c> with in-memory buffer + queue + bucket +
    /// breaker snapshots. Default <c>false</c> so production deployments
    /// don't leak internal state by accident; Development overrides via
    /// <c>appsettings.Development.json</c>. Phase-3 replaces the bare flag
    /// with proper admin-API auth.
    /// </summary>
    public bool DiagnosticsEnabled { get; init; }

    /// <summary>
    /// Circuit-breaker tuning for the per-<c>(tenant, channel)</c>
    /// Polly v8 pipeline (Sprint-5 plan U5).
    /// </summary>
    public sealed class BreakerSettings
    {
        /// <summary>
        /// Minimum number of actions inside <see cref="SamplingDurationSeconds"/>
        /// before the breaker considers a trip. Polly's default is 100;
        /// Sprint-5 lowers it so flash-sale fault detection is fast.
        /// </summary>
        public int MinimumThroughput { get; init; } = 5;

        /// <summary>
        /// Cool-down before the breaker transitions Open → HalfOpen and
        /// admits one probe call.
        /// </summary>
        public int BreakDurationSeconds { get; init; } = 60;

        /// <summary>
        /// Rolling window length over which Polly counts results to
        /// compute the trip ratio.
        /// </summary>
        public int SamplingDurationSeconds { get; init; } = 30;
    }
}
