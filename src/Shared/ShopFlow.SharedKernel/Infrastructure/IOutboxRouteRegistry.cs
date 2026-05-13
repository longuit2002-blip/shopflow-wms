namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Registry of CLR-type → <see cref="OutboxRoute"/> bindings per Sprint-4
/// plan U4 (K13 close). Populated at composition time by per-module
/// <c>AddXModule</c> extensions; consumed by
/// <see cref="MultiplexedOutboxDispatcher{TContext}"/> per row to decide
/// Publish vs Send.
/// </summary>
/// <remarks>
/// Unregistered types fall back to <see cref="OutboxRoute.PublishDefault"/>
/// so the existing Sprint-1/2/3 event paths require zero changes.
/// </remarks>
public interface IOutboxRouteRegistry
{
    /// <summary>
    /// Resolve the dispatch route for <paramref name="messageType"/>.
    /// Returns <see cref="OutboxRoute.PublishDefault"/> when nothing is
    /// registered.
    /// </summary>
    OutboxRoute Resolve(Type messageType);

    /// <summary>
    /// Register or replace the route for a CLR type. Last-write-wins so
    /// composition order across modules is not load-bearing (the wiring
    /// extension calls this).
    /// </summary>
    void Register(Type messageType, OutboxRoute route);
}
