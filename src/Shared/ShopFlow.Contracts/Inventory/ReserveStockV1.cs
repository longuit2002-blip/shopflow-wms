namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Saga-issued command from the Outbound fulfillment saga (U4) to the
/// Inventory module per Sprint-3-redux K1: one command per order with
/// <see cref="Lines"/> as the per-line list. The Inventory consumer
/// translates the N-line payload into ONE atomic call against
/// <c>IReservationRepository.TryReserveLinesAsync</c> — N rows inserted
/// in one CTE, not N sequential <c>TryReserveAsync</c> calls.
/// </summary>
/// <remarks>
/// <see cref="Ttl"/> is the reservation TTL (default 15 min per Tech
/// Design v3.0 §4.2). The saga sets it per shipping profile; the
/// Inventory consumer passes it straight through to the repository.
/// </remarks>
public sealed record ReserveStockV1(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<ReserveStockLineV1> Lines,
    TimeSpan Ttl
);

/// <summary>
/// Per-line payload for <see cref="ReserveStockV1"/>.
/// <see cref="OrderLineId"/> matches the Outbound <c>order_lines.id</c>
/// and lands on the Inventory ledger's composite UNIQUE
/// <c>(order_id, order_line_id)</c> per K10/K11.
/// </summary>
public sealed record ReserveStockLineV1(string OrderLineId, string Sku, int Qty);
