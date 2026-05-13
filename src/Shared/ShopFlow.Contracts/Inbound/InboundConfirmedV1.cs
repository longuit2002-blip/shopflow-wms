namespace ShopFlow.Contracts.Inbound;

/// <summary>
/// Cross-module integration event emitted by the Inbound module per
/// Sprint-2-redux plan R10: one event per confirmed receiving line.
/// Carries everything the Inventory consumer needs to apply the stock
/// change without a follow-up read against Inbound's tenant DB.
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
public sealed record InboundConfirmedV1(
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid ReceivingId,
    string Sku,
    int ActualQuantity,
    long BinId,
    Guid TenantId,
    DateTime OccurredAt
);
