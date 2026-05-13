namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Dispatch discipline for an <see cref="OutboxMessage"/> envelope per
/// Sprint-4 plan U4 (K13 close). <see cref="Publish"/> = fan-out event
/// semantics (MassTransit <c>IPublishEndpoint.Publish</c>);
/// <see cref="Send"/> = point-to-point command semantics
/// (<c>ISendEndpointProvider.GetSendEndpoint(...).Send</c>). The dispatcher
/// reads the registered <see cref="OutboxRoute.Kind"/> per row to decide.
/// </summary>
public enum SendKind
{
    /// <summary>Default fan-out — every interested consumer receives.</summary>
    Publish = 0,

    /// <summary>Point-to-point — exactly one consumer receives.</summary>
    Send = 1,
}
