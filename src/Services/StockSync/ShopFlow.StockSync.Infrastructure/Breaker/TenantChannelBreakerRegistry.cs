using System.Collections.Concurrent;
using Polly;
using Polly.CircuitBreaker;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Infrastructure.Pipeline;

namespace ShopFlow.StockSync.Infrastructure.Breaker;

/// <summary>
/// Sprint-5 plan U5 (R7) — per-<c>(tenant, channel)</c> Polly v8
/// circuit breaker registry. The dispatcher (
/// <c>PerTenantDispatcherService</c>) calls
/// <see cref="GetOrCreate"/> before invoking the marketplace adapter
/// so a downstream outage on one <c>(tenant, channel)</c> pair shuts
/// off its push pipeline without taking down sibling pairs. Sprint-5
/// noisy-neighbor scale gate hinges on this isolation (R7 + KTD3).
/// </summary>
/// <remarks>
/// <para>Each pair gets its own pipeline lazily on first access via
/// <see cref="ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>;
/// the pipeline is paired with its
/// <see cref="CircuitBreakerStateProvider"/> so
/// <see cref="GetState"/> can report Closed / Open / HalfOpen for the
/// U8 diagnostics endpoint without re-running a probe through the
/// pipeline.</para>
///
/// <para>Registered as <c>Singleton</c> in
/// <see cref="ServiceCollectionExtensions"/> (U8) — there is exactly
/// one registry per process so breaker state survives across consume
/// scopes and across the BackgroundService's per-tenant tasks.</para>
/// </remarks>
public sealed class TenantChannelBreakerRegistry
{
    private readonly ConcurrentDictionary<(Guid TenantId, string ChannelType), PushPipelineBundle> _bundles = new();
    private readonly PushPipelineFactory _factory;

    public TenantChannelBreakerRegistry(PushPipelineFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Returns the breaker pipeline for the given
    /// <c>(tenantId, channelType)</c>, building it on first request.
    /// The returned pipeline is safe to share across concurrent
    /// executes — Polly v8 pipelines are thread-safe by contract.
    /// </summary>
    public ResiliencePipeline<Result> GetOrCreate(Guid tenantId, string channelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelType);
        return GetOrBuildBundle(tenantId, channelType).Pipeline;
    }

    /// <summary>
    /// Returns the current circuit state for diagnostics — one of
    /// <c>"Closed"</c>, <c>"Open"</c>, <c>"HalfOpen"</c>, or
    /// <c>"Isolated"</c>. Building the pipeline on read keeps the
    /// dictionary consistent: the U8 endpoint can ask about a pair
    /// that hasn't dispatched yet and the answer is always
    /// <c>"Closed"</c> (the only state a fresh breaker can be in).
    /// </summary>
    public string GetState(Guid tenantId, string channelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelType);
        var state = GetOrBuildBundle(tenantId, channelType).StateProvider.CircuitState;
        return state switch
        {
            CircuitState.Closed => "Closed",
            CircuitState.Open => "Open",
            CircuitState.HalfOpen => "HalfOpen",
            CircuitState.Isolated => "Isolated",
            _ => state.ToString(),
        };
    }

    private PushPipelineBundle GetOrBuildBundle(Guid tenantId, string channelType)
    {
        return _bundles.GetOrAdd(
            (tenantId, channelType),
            static (key, factory) => factory.Build(key.TenantId, key.ChannelType),
            _factory
        );
    }
}
