using System.Collections.Concurrent;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Dictionary-backed <see cref="IOutboxRouteRegistry"/> per Sprint-4 plan U4
/// (K13 close). Registered as a singleton in <c>AddShopFlowDefaults</c> and
/// populated by per-module <c>AddOutboxRoute&lt;T&gt;</c> calls at composition
/// time. Reads are lock-free via <see cref="ConcurrentDictionary{TKey, TValue}"/>;
/// writes are expected to land before the host starts processing requests.
/// </summary>
public sealed class OutboxRouteRegistry : IOutboxRouteRegistry
{
    private readonly ConcurrentDictionary<Type, OutboxRoute> _routes = new();

    /// <summary>
    /// Default constructor — modules register via
    /// <c>services.AddOutboxRoute&lt;T&gt;(...)</c> in their composition root,
    /// which the DI container surfaces via <see cref="OutboxRouteSeed"/>
    /// instances applied through the alternate constructor below.
    /// </summary>
    public OutboxRouteRegistry() { }

    /// <summary>
    /// DI-driven seed constructor — receives every
    /// <see cref="OutboxRouteSeed"/> registered via
    /// <c>services.AddOutboxRoute&lt;T&gt;(...)</c> at composition time and
    /// applies them in registration order (last-write-wins).
    /// </summary>
    public OutboxRouteRegistry(IEnumerable<OutboxRouteSeed> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        foreach (var seed in seeds)
        {
            _routes[seed.MessageType] = seed.Route;
        }
    }

    public OutboxRoute Resolve(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        return _routes.TryGetValue(messageType, out var route) ? route : OutboxRoute.PublishDefault;
    }

    public void Register(Type messageType, OutboxRoute route)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(route);
        _routes[messageType] = route;
    }

    /// <summary>
    /// Snapshot for diagnostics + tests. Order is unspecified.
    /// </summary>
    public IReadOnlyDictionary<Type, OutboxRoute> Snapshot()
    {
        return new Dictionary<Type, OutboxRoute>(_routes);
    }
}
