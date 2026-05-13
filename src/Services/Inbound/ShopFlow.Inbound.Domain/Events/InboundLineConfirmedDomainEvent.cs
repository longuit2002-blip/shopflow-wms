using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Domain.Events;

/// <summary>
/// Raised on the <see cref="Receiving"/> aggregate when a line is
/// confirmed. The shape is intentionally identical to the cross-module
/// integration event <c>ShopFlow.Contracts.Inbound.InboundConfirmedV1</c>
/// (introduced in Sprint-2-redux U6) so the
/// <c>OutboxInterceptor</c>-emitted JSON deserializes cleanly on the
/// Inventory consumer side. U6 may collapse this type into the contract
/// directly; for U3 it lives module-internal so Domain has no dependency
/// on the cross-module contracts project.
/// </summary>
public sealed record InboundLineConfirmedDomainEvent(
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid ReceivingId,
    string Sku,
    int ActualQuantity,
    long BinId,
    DateTime OccurredAt
) : IDomainEvent;
