namespace ShopFlow.Inventory.Application.Dtos;

/// <summary>
/// One ledger entry shape for <c>GET /api/v1/inventory/skus/{sku}/ledger</c>
/// (Sprint-6 plan U7). Reads from the append-only <c>reservations_ledger</c>
/// table; the handler computes <c>RunningBalance</c> server-side as a
/// cumulative SUM over <c>Quantity * sign(status)</c> ordered ASC by
/// <c>Timestamp</c>.
/// </summary>
/// <param name="Id">Ledger row id (GUID; for drawer detail anchoring).</param>
/// <param name="OrderId">Reservation order id (mono-rendered in drawer).</param>
/// <param name="OrderLineId">Sprint-3-redux multi-line order id; defaults to "_default" for single-line.</param>
/// <param name="Status">Pending / Confirmed / Released / Expired.</param>
/// <param name="Quantity">Reserved quantity (always positive in ledger).</param>
/// <param name="Timestamp">Event time (created_at / confirmed_at / released_at / expired_at, whichever applies).</param>
/// <param name="RunningBalance">Cumulative balance after this entry.</param>
public sealed record SkuLedgerEntryDto(
    Guid Id,
    string OrderId,
    string OrderLineId,
    string Status,
    int Quantity,
    DateTime Timestamp,
    int RunningBalance
);

public sealed record SkuLedgerDto(IReadOnlyList<SkuLedgerEntryDto> Items, string? NextCursor);
