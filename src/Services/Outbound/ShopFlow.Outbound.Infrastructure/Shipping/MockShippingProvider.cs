using Polly;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Shipping;

/// <summary>
/// Mock carrier implementation per Sprint-3-redux U6 / K5. Simulates a
/// real shipping API: 1-3 s wall-time delay per call (configurable), a
/// configurable transient-fail rate (default 5%), wrapped in a Polly v8
/// <see cref="ResiliencePipeline"/> retry strategy.
/// </summary>
/// <remarks>
/// <para>Composition: the constructor receives a pre-built
/// <see cref="ResiliencePipeline"/> (built once at DI-time via
/// <see cref="ResiliencePipelineBuilder"/> in
/// <c>OutboundServiceCollectionExtensions.AddOutboundModule</c>). The
/// inner call is the simulated carrier hit; the pipeline wraps it with
/// 3 retries on <see cref="TransientShippingException"/>. After 1
/// initial + 3 retries (4 attempts total), an unhandled
/// <see cref="TransientShippingException"/> propagates — the controller
/// catches and maps to 503.</para>
///
/// <para>Test ergonomics: the static <see cref="WithFlakeRate"/>
/// builder constructs a provider with a deterministic flake rate
/// (typically 0 for always-succeed or 1 for always-fail) so unit tests
/// can prove Polly's retry-then-success + retry-exhaust paths without
/// flake. Production binding uses a 5% rate.</para>
///
/// <para>Random sources use <see cref="Random.Shared"/> per
/// AGENTS.md §3.21 (no captured-stale-seed <c>new Random()</c>) by default.
/// Sprint-12.5 U4 added the 5-arg ctor accepting an optional
/// <c>Func&lt;double&gt;</c> for deterministic flake-sequencing in
/// tier-3 carrier-retry E2E tests; production binding leaves it null so
/// <see cref="Random.Shared"/> remains the canonical source.</para>
/// </remarks>
public sealed class MockShippingProvider : IMockShippingProvider
{
    /// <summary>Default transient-fail rate per K5 / U6 plan spec (5%).</summary>
    public const double DefaultFlakeRate = 0.05;

    /// <summary>Default per-call lower-bound delay (1 s) per the plan spec.</summary>
    public const int DefaultMinDelayMs = 1000;

    /// <summary>Default per-call upper-bound delay (3 s, exclusive) per the plan spec.</summary>
    public const int DefaultMaxDelayMsExclusive = 3001;

    private readonly ResiliencePipeline _pipeline;
    private readonly double _flakeRate;
    private readonly int _minDelayMs;
    private readonly int _maxDelayMsExclusive;
    private readonly Func<double> _randomSource;

    public MockShippingProvider(ResiliencePipeline pipeline)
        : this(
            pipeline,
            DefaultFlakeRate,
            DefaultMinDelayMs,
            DefaultMaxDelayMsExclusive,
            randomSource: null
        ) { }

    public MockShippingProvider(
        ResiliencePipeline pipeline,
        double flakeRate,
        int minDelayMs,
        int maxDelayMsExclusive
    )
        : this(pipeline, flakeRate, minDelayMs, maxDelayMsExclusive, randomSource: null) { }

    /// <summary>
    /// Sprint-12.5 U4 — additive 5-arg ctor accepting an optional
    /// <paramref name="randomSource"/> for deterministic flake-sequencing
    /// in tier-3 carrier-retry E2E tests. When null, falls back to
    /// <see cref="Random.Shared"/>.
    /// </summary>
    /// <remarks>
    /// <para>The injected lambda is invoked once per
    /// <see cref="InnerCreateLabelAsync"/> attempt for the FLAKE decision.
    /// The CARRIER DELAY in <see cref="InnerCreateLabelAsync"/> still uses
    /// <see cref="Random.Shared"/> — wall-time variance is independent of
    /// failure determinism; AE6's assertion is on call count not exact
    /// wall-time.</para>
    ///
    /// <para>Polly v8 retry continuations resume on ThreadPool workers,
    /// so any backing collection the lambda closes over MUST be
    /// thread-safe (e.g. <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>
    /// — NOT <see cref="System.Collections.Generic.Queue{T}"/>).</para>
    /// </remarks>
    public MockShippingProvider(
        ResiliencePipeline pipeline,
        double flakeRate,
        int minDelayMs,
        int maxDelayMsExclusive,
        Func<double>? randomSource
    )
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (flakeRate < 0 || flakeRate > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flakeRate),
                "flake_rate must be in [0, 1]."
            );
        }
        if (minDelayMs < 0 || maxDelayMsExclusive <= minDelayMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDelayMsExclusive),
                "max_delay_ms_exclusive must be > min_delay_ms."
            );
        }
        _pipeline = pipeline;
        _flakeRate = flakeRate;
        _minDelayMs = minDelayMs;
        _maxDelayMsExclusive = maxDelayMsExclusive;
        _randomSource = randomSource ?? (() => Random.Shared.NextDouble());
    }

    /// <inheritdoc />
    public Task<ShippingLabel> CreateLabelAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Polly v8: ExecuteAsync with a ValueTask-returning delegate is the
        // canonical shape. The pipeline catches TransientShippingException
        // per its configured ShouldHandle predicate, applies the 3-retry
        // constant-backoff strategy, then re-throws on exhaustion.
        return _pipeline
            .ExecuteAsync(
                async cancellationToken =>
                    await InnerCreateLabelAsync(order, cancellationToken).ConfigureAwait(false),
                ct
            )
            .AsTask();
    }

    private async ValueTask<ShippingLabel> InnerCreateLabelAsync(Order order, CancellationToken ct)
    {
        // Simulate the 1-3 s carrier latency. Random.Shared per §3.21.
        var delay = Random.Shared.Next(_minDelayMs, _maxDelayMsExclusive);
        await Task.Delay(delay, ct).ConfigureAwait(false);

        if (_randomSource() < _flakeRate)
        {
            throw new TransientShippingException(
                $"Mock carrier transient failure for order {order.Id}."
            );
        }

        var trackingNumber = "TRK-" + Guid.NewGuid().ToString("N")[..16];
        var labelUrl = $"https://mock-carrier.example/labels/{trackingNumber}.pdf";
        return new ShippingLabel(labelUrl, trackingNumber);
    }

    /// <summary>
    /// Builder for tests: returns a provider with a deterministic flake
    /// rate (0 = always succeed, 1 = always fail) using the same pipeline.
    /// </summary>
    public static MockShippingProvider WithFlakeRate(
        ResiliencePipeline pipeline,
        double flakeRate
    ) => new(pipeline, flakeRate, DefaultMinDelayMs, DefaultMaxDelayMsExclusive);

    /// <summary>
    /// Builder for tests: returns a provider with deterministic flake
    /// rate + a custom delay window (e.g. short delays so MockShippingProviderTests
    /// run sub-second).
    /// </summary>
    public static MockShippingProvider WithFlakeRateAndDelay(
        ResiliencePipeline pipeline,
        double flakeRate,
        int minDelayMs,
        int maxDelayMsExclusive
    ) => new(pipeline, flakeRate, minDelayMs, maxDelayMsExclusive);

    /// <summary>
    /// Sprint-12.5 U4 — builder for tier-3 carrier-retry E2E tests:
    /// returns a provider with deterministic flake rate + custom delay
    /// window + an injected <paramref name="randomSource"/> for
    /// deterministic flake-sequencing. The lambda is invoked once per
    /// <c>InnerCreateLabelAsync</c> attempt for the FLAKE decision; pass a
    /// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>-backed
    /// dequeue function for thread-safe pre-seeded value sequences
    /// (Polly v8 retry continuations resume on ThreadPool workers).
    /// </summary>
    public static MockShippingProvider WithFlakeRateDelayAndRandom(
        ResiliencePipeline pipeline,
        double flakeRate,
        int minDelayMs,
        int maxDelayMsExclusive,
        Func<double> randomSource
    ) => new(pipeline, flakeRate, minDelayMs, maxDelayMsExclusive, randomSource);
}
