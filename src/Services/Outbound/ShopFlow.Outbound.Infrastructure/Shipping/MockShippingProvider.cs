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
/// AGENTS.md §3.21 (no captured-stale-seed <c>new Random()</c>).</para>
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

    public MockShippingProvider(ResiliencePipeline pipeline)
        : this(pipeline, DefaultFlakeRate, DefaultMinDelayMs, DefaultMaxDelayMsExclusive) { }

    public MockShippingProvider(
        ResiliencePipeline pipeline,
        double flakeRate,
        int minDelayMs,
        int maxDelayMsExclusive
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

    private async ValueTask<ShippingLabel> InnerCreateLabelAsync(
        Order order,
        CancellationToken ct
    )
    {
        // Simulate the 1-3 s carrier latency. Random.Shared per §3.21.
        var delay = Random.Shared.Next(_minDelayMs, _maxDelayMsExclusive);
        await Task.Delay(delay, ct).ConfigureAwait(false);

        if (Random.Shared.NextDouble() < _flakeRate)
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
    public static MockShippingProvider WithFlakeRate(ResiliencePipeline pipeline, double flakeRate) =>
        new(pipeline, flakeRate, DefaultMinDelayMs, DefaultMaxDelayMsExclusive);

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
}
