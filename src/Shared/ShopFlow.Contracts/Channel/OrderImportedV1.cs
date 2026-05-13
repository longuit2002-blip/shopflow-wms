namespace ShopFlow.Contracts.Channel;

/// <summary>
/// Cross-module command emitted by Channel.IngestWebhookService (Sprint-4
/// U8) and consumed by Outbound.OrderImportedConsumer. K13 (Sprint-4 U4)
/// routes this as <c>SendKind.Send</c> — point-to-point command semantics
/// — so exactly one Outbound consumer creates the resulting <c>Order</c>
/// row. Idempotency carries via <see cref="ChannelExternalOrderId"/> →
/// <c>Order.ChannelExternalOrderId</c> UNIQUE (Sprint-3-redux U2).
/// </summary>
/// <param name="OrderId">Pre-assigned Order id (the Channel side mints it).</param>
/// <param name="TenantId">Tenant scope — also carried on the MT envelope header.</param>
/// <param name="ChannelId">Source channel id (control-plane <c>channel_connections.channel_id</c>).</param>
/// <param name="ChannelExternalOrderId">
/// Marketplace-side order id. Doubles as the idempotency key on the
/// Outbound side (UNIQUE on <c>orders</c> table from Sprint-3-redux U2).
/// </param>
/// <param name="ShippingProfile">Operator-configured shipping profile name.</param>
/// <param name="Lines">Order lines — internal SKUs already resolved via product mapping.</param>
/// <param name="OccurredAt">Marketplace-side timestamp (Shopee envelope's <c>timestamp</c>).</param>
public sealed record OrderImportedV1(
    Guid OrderId,
    Guid TenantId,
    Guid ChannelId,
    string ChannelExternalOrderId,
    string ShippingProfile,
    IReadOnlyList<OrderImportedLineV1> Lines,
    DateTime OccurredAt
);

/// <summary>
/// One line on an imported order — internal SKU resolved at Channel-side
/// product mapping time so the Outbound consumer doesn't re-do the work.
/// Unmappable lines fail the whole import at the receiver per Sprint-4
/// plan Open Questions (status set to Failed on the webhook_events row,
/// no OrderImportedV1 emitted).
/// </summary>
public sealed record OrderImportedLineV1(string Sku, int Qty);
