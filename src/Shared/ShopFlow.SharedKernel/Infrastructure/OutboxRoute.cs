namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Dispatch metadata for an <see cref="OutboxMessage"/> CLR type per Sprint-4
/// plan U4 (K13 close). The <see cref="MultiplexedOutboxDispatcher{TContext}"/>
/// resolves a route via <see cref="IOutboxRouteRegistry"/> before each
/// publish; unregistered types default to <c>Publish</c> so Sprint-1/2/3
/// existing event paths remain unchanged.
/// </summary>
/// <param name="Kind">Dispatch discipline (<see cref="SendKind.Publish"/> default).</param>
/// <param name="Exchange">
/// Optional RabbitMQ exchange override. Null = MassTransit's default exchange
/// naming convention. Reserved for cross-vhost / advanced topologies.
/// </param>
/// <param name="RoutingKey">
/// Optional explicit destination address. For <see cref="SendKind.Send"/>
/// this becomes the queue name (e.g. <c>"order-imported-v1"</c>); for
/// <see cref="SendKind.Publish"/> this is ignored and MassTransit derives
/// the exchange from the message type.
/// </param>
public sealed record OutboxRoute(SendKind Kind, string? Exchange = null, string? RoutingKey = null)
{
    /// <summary>
    /// Backward-compatible default for unregistered CLR types — preserves
    /// the Sprint-1/2/3 publish-everything behaviour.
    /// </summary>
    public static readonly OutboxRoute PublishDefault = new(SendKind.Publish);
}
