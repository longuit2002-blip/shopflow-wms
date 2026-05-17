using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Application.Options;

namespace ShopFlow.StockSync.Infrastructure.Pipeline;

/// <summary>
/// Builds the Polly v8 push pipeline for one
/// <c>(tenant, channel)</c> pair per Sprint-5 plan U5 / R7. Single
/// strategy in the pipeline: <see cref="CircuitBreakerStrategyOptions{TResult}"/>
/// keyed on <see cref="Result"/> failure or thrown exception. The
/// factory is registered as a <c>Singleton</c> in U8 so
/// <see cref="Breaker.TenantChannelBreakerRegistry"/> can call into it
/// once per uncached pair.
/// </summary>
/// <remarks>
/// <para>One factory call yields one <see cref="ResiliencePipeline{TResult}"/>
/// plus the matching <see cref="CircuitBreakerStateProvider"/>; the
/// caller stores both so it can expose state ("Closed" / "Open" /
/// "HalfOpen") for the U8 diagnostics endpoint without round-tripping
/// through Polly's manual control APIs.</para>
///
/// <para>Trip predicate matches both shapes a marketplace push can
/// fail in: an explicit <see cref="Result"/> failure (the adapter
/// caught its own 5xx and returned a stable error code) and an
/// uncaught exception (network reset, DNS failure). Both count
/// toward <see cref="StockSyncOptions.BreakerSettings.MinimumThroughput"/>.</para>
///
/// <para>Counter / OpenTelemetry export of <c>OnOpened</c>
/// transitions is Phase-3; Sprint-5 logs at <c>Warning</c> so the
/// Aspire dashboard surfaces breaker trips during noisy-neighbor load
/// tests.</para>
/// </remarks>
public sealed class PushPipelineFactory
{
    private readonly StockSyncOptions.BreakerSettings _settings;
    private readonly ILogger<PushPipelineFactory> _logger;

    public PushPipelineFactory(
        IOptions<StockSyncOptions> options,
        ILogger<PushPipelineFactory> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var breaker = options.Value.Breaker;
        if (breaker.MinimumThroughput <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                breaker.MinimumThroughput,
                "StockSyncOptions.Breaker.MinimumThroughput must be > 0."
            );
        }
        if (breaker.BreakDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                breaker.BreakDurationSeconds,
                "StockSyncOptions.Breaker.BreakDurationSeconds must be > 0."
            );
        }
        if (breaker.SamplingDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                breaker.SamplingDurationSeconds,
                "StockSyncOptions.Breaker.SamplingDurationSeconds must be > 0."
            );
        }

        _settings = breaker;
        _logger = logger;
    }

    /// <summary>
    /// Build a fresh breaker pipeline + state provider for the given
    /// <c>(tenant, channel)</c>. The state provider is keyed to the
    /// returned pipeline; the caller stores the pair so it can read
    /// <see cref="CircuitBreakerStateProvider.CircuitState"/> later.
    /// </summary>
    /// <param name="tenantId">Tenant the pipeline belongs to — used for log context only; the pipeline itself is identity-free.</param>
    /// <param name="channelType">Channel slug the pipeline belongs to.</param>
    public PushPipelineBundle Build(Guid tenantId, string channelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelType);

        var stateProvider = new CircuitBreakerStateProvider();
        var manualControl = new CircuitBreakerManualControl();

        var pipeline = new ResiliencePipelineBuilder<Result>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<Result>
            {
                // FailureRatio = 1.0 turns the breaker into a pure
                // consecutive-failure detector inside the sampling
                // window: every action must fail for the breaker to
                // trip. Combined with MinimumThroughput = 5 this means
                // 5 back-to-back failures inside SamplingDuration =
                // Open.
                FailureRatio = 1.0,
                MinimumThroughput = _settings.MinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(_settings.BreakDurationSeconds),
                SamplingDuration = TimeSpan.FromSeconds(_settings.SamplingDurationSeconds),
                StateProvider = stateProvider,
                ManualControl = manualControl,
                ShouldHandle = new PredicateBuilder<Result>()
                    .Handle<Exception>()
                    .HandleResult(static r => r is not null && !r.IsSuccess),
                OnOpened = args =>
                {
                    _logger.LogWarning(
                        "Circuit breaker OPENED for tenant {TenantId} channel {ChannelType} (break duration {BreakSeconds}s).",
                        tenantId,
                        channelType,
                        (int)args.BreakDuration.TotalSeconds
                    );
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker CLOSED for tenant {TenantId} channel {ChannelType}.",
                        tenantId,
                        channelType
                    );
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker HALF-OPEN probe for tenant {TenantId} channel {ChannelType}.",
                        tenantId,
                        channelType
                    );
                    return ValueTask.CompletedTask;
                },
            })
            .Build();

        return new PushPipelineBundle(pipeline, stateProvider);
    }
}

/// <summary>
/// Pairs a built Polly v8 <see cref="ResiliencePipeline{TResult}"/>
/// with its <see cref="CircuitBreakerStateProvider"/> so callers can
/// inspect circuit state without keeping Polly internals on their
/// public surface.
/// </summary>
public sealed record PushPipelineBundle(
    ResiliencePipeline<Result> Pipeline,
    CircuitBreakerStateProvider StateProvider
);
