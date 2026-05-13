namespace ShopFlow.Inbound.Api.Contracts;

/// <summary>
/// Request DTOs for the Inbound HTTP surface (Sprint-2-redux U8). Records
/// only — controllers map to / from the Domain types.
/// </summary>
public sealed record CreatePoRequest(
    string SupplierRef,
    DateTime ExpectedDeliveryAt,
    IReadOnlyList<CreatePoLineRequest> Lines
);

public sealed record CreatePoLineRequest(string Sku, int ExpectedQty);

public sealed record CancelPoRequest(string Reason);

public sealed record ConfirmReceivingLineRequest(
    Guid? ReceivingId,
    Guid PurchaseOrderLineId,
    int ActualQty,
    long SuggestedBinId,
    long ActualBinId
);

public sealed record PoLineResponse(Guid Id, string Sku, int ExpectedQty, int ReceivedQty);

public sealed record PoResponse(
    Guid Id,
    string SupplierRef,
    DateTime ExpectedDeliveryAt,
    string Status,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    DateTime? CancelledAt,
    IReadOnlyList<PoLineResponse> Lines
);

public sealed record ConfirmReceivingLineResponse(
    Guid ReceivingId,
    Guid ReceivingLineId,
    bool Idempotent,
    bool TicketCreated
);
