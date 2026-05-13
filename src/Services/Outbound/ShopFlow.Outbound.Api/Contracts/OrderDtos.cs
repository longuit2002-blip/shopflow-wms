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
