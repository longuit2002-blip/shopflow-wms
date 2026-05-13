namespace ShopFlow.Contracts.Outbound;

/// <summary>
/// Cross-module integration event emitted by the Outbound module per
/// Sprint-3-redux U2/U3 when a customer order is persisted. Carries the
/// full order shape needed by the fulfillment saga (U4) and downstream
/// listeners (Analytics, etc) without a follow-up read against Outbound's
/// tenant DB.
/// </summary>
/// <remarks>
/// Per ADR-0002 + AGENTS.md §10 cross-module integration events live in
/// <c>ShopFlow.Contracts</c> and are wire-compatible record types
/// (immutable, JSON-serialisable, no domain or framework dependencies).
/// The <c>V1</c> suffix allows the contract to evolve via parallel
/// <c>V2</c> without breaking consumers. <c>TenantId</c> flows through
/// the MassTransit envelope header in addition to being carried on the
/// payload for diagnostic / cross-tenant assertions in tests.
/// </remarks>
public sealed record OrderPlacedV1(
    Guid OrderId,
    Guid TenantId,
    string ChannelExternalOrderId,
    string ShippingProfile,
    IReadOnlyList<OrderPlacedLineV1> Lines,
    DateTime OccurredAt
);

/// <summary>
/// Line-level payload for <see cref="OrderPlacedV1"/>. <see cref="OrderLineId"/>
/// is the Outbound <c>order_lines.id</c> stringified — used as the
/// <c>order_line_id</c> on the Inventory ledger's composite UNIQUE
/// <c>(order_id, order_line_id)</c> per K10/K11.
/// </summary>
public sealed record OrderPlacedLineV1(
    string OrderLineId,
    string Sku,
    int Qty,
    int? ExpectedWeight
);
