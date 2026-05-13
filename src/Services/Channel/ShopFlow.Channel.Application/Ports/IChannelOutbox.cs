namespace ShopFlow.Channel.Application.Ports;

/// <summary>
/// Application-layer write port over the Channel module's per-tenant
/// <c>channel_outbox_messages</c> table (Sprint-2.5 per-module prefix).
/// Lets the webhook ingress orchestrator enqueue a typed cross-module
/// payload (e.g. <c>OrderImportedV1</c> in U8) for the multiplexed
/// dispatcher to publish. Direct mirror of Sprint-3-redux's
/// <c>IOutboundOutbox</c>.
/// </summary>
public interface IChannelOutbox
{
    /// <summary>
    /// Enqueue an integration event for publish. The row flows on the
    /// caller's open EF transaction — committed atomically with the
    /// webhook_events insert via <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    Task AppendAsync(string eventType, object payload, CancellationToken ct);
}
