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
public sealed record MarkPickFailedRequest(string? Reason);
