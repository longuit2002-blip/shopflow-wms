using System.ComponentModel.DataAnnotations;

namespace ShopFlow.Outbound.Api.Contracts;

/// <summary>
/// Request + response DTOs for the Outbound HTTP surface (Sprint-3-redux
/// U2). Records only — the controller maps to / from the Domain types.
/// Mirrors Sprint-2-redux's <c>InboundDtos</c> shape.
/// </summary>
public sealed record CreateOrderRequest(
    string ChannelExternalOrderId,
    string ShippingProfile,
    IReadOnlyList<CreateOrderLineRequest> Lines
);

public sealed record CreateOrderLineRequest(string Sku, int Qty, int? ExpectedWeight);

public sealed record OrderLineResponse(Guid Id, string Sku, int Qty, int? ExpectedWeight);

public sealed record OrderResponse(
    Guid Id,
    string ChannelExternalOrderId,
    string ShippingProfile,
    string Status,
    int? ExpectedWeightTotal,
    int? ActualWeightTotal,
    string? LabelUrl,
    string? TrackingNumber,
    Guid? PickWaveId,
    IReadOnlyList<OrderLineResponse> Lines
);

/// <summary>
/// Sprint-3-redux U6 — <c>POST /confirm-pack</c> body. The packer reports
/// the actual packed weight; the controller computes the variance vs.
/// <c>expected_weight_total</c>.
/// </summary>
public sealed record ConfirmPackRequest(int ActualWeightTotal);

/// <summary>
/// Sprint-3-redux U6 — <c>POST /confirm-pack</c> response. Includes
/// the updated order shape plus the weight-warning flag + signed
/// variance percentage (null when <c>expected_weight_total</c> is
/// unset).
/// </summary>
public sealed record ConfirmPackResponse(
    OrderResponse Order,
    bool WeightWarning,
    double? WeightVariancePct
);

/// <summary>
/// Sprint-3-redux U6 — <c>POST /confirm-ship</c> response. Surfaces the
/// label URL + tracking number returned by the (mocked) carrier so the
/// operator can hand them to the courier.
/// </summary>
public sealed record ConfirmShipResponse(
    string LabelUrl,
    string TrackingNumber,
    OrderResponse Order
);

/// <summary>
/// Sprint-3-redux U7 — <c>POST /mark-pick-failed</c> body. The operator
/// reports an optional human-readable reason; the saga uses it for
/// diagnostic logging only (no pick_failed_reason column in the U1
/// schema — Phase-2 candidate). Empty / whitespace reason is allowed.
/// </summary>
/// <remarks>
/// Sprint-12.5 KTD10 retrofit — <c>Reason</c> capped at 1000 characters
/// via <see cref="MaxLengthAttribute"/>. Closes the inherited DoS / outbox-
/// bloat vector flagged by Sprint-12.5 doc-review (a malicious Picker
/// could otherwise submit ~10MB reasons).
/// </remarks>
public sealed record MarkPickFailedRequest([property: MaxLength(1000)] string? Reason);

/// <summary>
/// Sprint-12.5 U3 — <c>POST /mark-ship-failed</c> body. Operator reports
/// the carrier rejected the label / the package is damaged pre-ship.
/// Mirrors <see cref="MarkPickFailedRequest"/> shape including the 1000-
/// character <see cref="MaxLengthAttribute"/> cap (KTD10).
/// </summary>
public sealed record MarkShipFailedRequest([property: MaxLength(1000)] string? Reason);

// ── Sprint-7 U4 — Orders screen wire-shape ──────────────────────────────
// PascalCase wire stays unchanged (Sprint-6 KTD4). DTOs map from the
// Application-layer read models (OrderListRow / OrderDetailReadModel /
// OrderTransitionReadModel) via static helpers below.

/// <summary>
/// Sprint-7 U4 — one row on the Orders list screen
/// (<c>GET /api/outbound/orders</c>).
/// </summary>
/// <param name="Id">Order id (uuid).</param>
/// <param name="ChannelExternalOrderId">Raw channel-side reference.</param>
/// <param name="Channel">Display label parsed from <c>ChannelExternalOrderId</c>'s prefix.</param>
/// <param name="LineCount">Number of <c>order_lines</c> rows.</param>
/// <param name="CurrentSagaState">Saga's current state string; null until first <c>OrderPlacedV1</c> consume.</param>
/// <param name="Age">Wall-time since <c>orders.created_at</c> at the moment the row was fetched.</param>
/// <param name="LastTransitionAt">Max <c>outbound_saga_transitions.occurred_at</c> for this order; null when none recorded.</param>
public sealed record OrderListItemDto(
    Guid Id,
    string ChannelExternalOrderId,
    string Channel,
    int LineCount,
    string? CurrentSagaState,
    TimeSpan Age,
    DateTime? LastTransitionAt
);

/// <summary>
/// Sprint-7 U4 — paginated response for <c>GET /api/outbound/orders</c>.
/// </summary>
public sealed record OrderListResponse(IReadOnlyList<OrderListItemDto> Items, int TotalCount);

/// <summary>
/// Sprint-7 U4 — full detail for <c>GET /api/outbound/orders/{id}</c>.
/// Carries the full <c>Order</c> shape (mirrors <see cref="OrderResponse"/>)
/// + the saga's current state + creation/update timestamps + parsed channel
/// label.
/// </summary>
public sealed record OrderDetailDto(
    Guid Id,
    string ChannelExternalOrderId,
    string Channel,
    string ShippingProfile,
    string Status,
    string? CurrentSagaState,
    int? ExpectedWeightTotal,
    int? ActualWeightTotal,
    string? LabelUrl,
    string? TrackingNumber,
    Guid? PickWaveId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<OrderLineResponse> Lines
);

/// <summary>
/// Sprint-7 U4 — one audit row for <c>GET /api/outbound/orders/{id}/transitions</c>.
/// Per doc-review decision #3 the <c>CorrelationId</c> column on
/// <c>outbound_saga_transitions</c> (U1 schema) propagates to the wire so
/// the frontend can hyperlink the row into the trace explorer (R14).
/// </summary>
public sealed record OrderTransitionDto(
    Guid Id,
    Guid OrderId,
    string FromState,
    string ToState,
    DateTime OccurredAt,
    string EventType,
    string CorrelationId
);

/// <summary>
/// Sprint-7 U4 — KPI strip on the Orders screen
/// (<c>GET /api/outbound/orders/kpis</c>). Four aggregate counts the
/// fulfillment dashboard renders top-of-page.
/// </summary>
/// <param name="ActiveOrders">Orders in any non-terminal state.</param>
/// <param name="AwaitingPick">Orders currently sitting in AwaitingPick.</param>
/// <param name="AwaitingShip">Orders currently sitting in AwaitingShip.</param>
/// <param name="FailedToday">Orders in Cancelled state with <c>created_at</c> ≥ start-of-UTC-today.</param>
public sealed record OrderKpiResponse(
    int ActiveOrders,
    int AwaitingPick,
    int AwaitingShip,
    int FailedToday
);

/// <summary>
/// Sprint-7 U4 — dev-mode seed body for
/// <c>POST /api/outbound/orders/seed</c>. Returns 404 outside Development.
/// </summary>
/// <param name="LineCount">Number of synthesized order lines (default 3, clamped 1-50).</param>
/// <param name="ChannelPrefix">Optional channel-id prefix; default <c>"SEED_"</c> yields <c>"Direct"</c> channel labels.</param>
public sealed record SeedOrderRequest(int LineCount = 3, string? ChannelPrefix = null);
