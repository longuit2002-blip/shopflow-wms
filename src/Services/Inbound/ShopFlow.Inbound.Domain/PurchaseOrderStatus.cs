namespace ShopFlow.Inbound.Domain;

/// <summary>
/// Lifecycle states of a purchase order per Sprint-2-redux plan R1. One-way
/// transitions: <c>Draft</c> → <c>Open</c> → <c>PartiallyReceived</c> → <c>Closed</c>;
/// <c>Cancelled</c> as alternate terminal from <c>Draft</c> or <c>Open</c>.
/// State machine bodies land in U2 (<see cref="PurchaseOrder"/> methods).
/// </summary>
public enum PurchaseOrderStatus
{
    Draft = 0,
    Open = 1,
    PartiallyReceived = 2,
    Closed = 3,
    Cancelled = 4,
}
