namespace ShopFlow.Channel.Application.Webhooks;

/// <summary>
/// Channel-internal intermediate shape produced by
/// <see cref="Adapters.IChannelAdapter.ParseOrderCreated"/> per Sprint-4.5
/// plan U1. The adapter knows marketplace shapes (external SKUs, raw line
/// quantities, carrier names); the orchestrator (U3) consumes this draft,
/// resolves each <see cref="ExternalOrderLine.ExternalSku"/> through
/// <c>IProductMappingService</c>, and assembles the canonical
/// <c>OrderImportedV1</c> contract.
/// </summary>
/// <remarks>
/// <para>Adapter does not know about <c>IProductMappingService</c> or
/// <c>OrderImportedV1.InternalSku</c> on purpose — keeping marketplace
/// shape concerns inside the adapter and internal-SKU resolution inside
/// the orchestrator preserves the AGENTS.md §2 layering. Sprint-6 Lazada
/// implements the same method against Lazada's order shape; the draft
/// type is marketplace-agnostic.</para>
/// <para>Field names map to the real Shopee Open Platform v2 wire shape
/// per <c>tests/fixtures/channels/shopee/webhook-order-created.json</c>:
/// <c>data.ordersn</c> → <see cref="ChannelExternalOrderId"/>,
/// <c>data.package_list[0].shipping_carrier</c> → <see cref="ShippingProfile"/>,
/// <c>data.items[].item_sku</c> + <c>model_quantity_purchased</c> →
/// <see cref="ExternalOrderLine"/>.</para>
/// </remarks>
/// <param name="ChannelExternalOrderId">
/// Marketplace-side order id (Shopee <c>ordersn</c>). The idempotency
/// anchor on the Outbound side (UNIQUE on <c>orders.channel_external_order_id</c>
/// from Sprint-3-redux U2).
/// </param>
/// <param name="ShippingProfile">
/// Operator-side shipping profile label. Sprint-4.5 maps Shopee
/// <c>package_list[0].shipping_carrier</c> verbatim; the operator-side
/// profile catalog lookup is Sprint-6+ work.
/// </param>
/// <param name="Lines">
/// Order lines as the marketplace reported them. Non-empty by construction
/// (the parser fails the whole import on an empty <c>items</c> array).
/// </param>
public sealed record ExternalOrderDraft(
    string ChannelExternalOrderId,
    string ShippingProfile,
    IReadOnlyList<ExternalOrderLine> Lines
);

/// <param name="ExternalSku">
/// Marketplace-side SKU (Shopee <c>item_sku</c>). Resolved to the internal
/// SKU by the orchestrator via <c>IProductMappingService.ResolveAsync</c>.
/// </param>
/// <param name="Qty">
/// Line quantity (Shopee <c>model_quantity_purchased</c>). Guaranteed
/// positive by the parser; non-positive quantities fail the whole import.
/// </param>
public sealed record ExternalOrderLine(string ExternalSku, int Qty);
