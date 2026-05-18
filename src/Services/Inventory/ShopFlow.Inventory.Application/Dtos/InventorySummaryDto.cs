namespace ShopFlow.Inventory.Application.Dtos;

/// <summary>
/// KPI strip shape for <c>GET /api/v1/inventory/summary</c> (Sprint-6 plan
/// U7 — Backend Gap R21 closure). Aggregates the entire tenant's
/// inventory in a single trip so the Inventory screen's polling loop
/// (2 s cadence) doesn't N+1 across the SKU table.
/// </summary>
/// <param name="TotalSkus">Count of SKU rows in <c>stock_items</c>.</param>
/// <param name="TotalAvailable">SUM(available) — units physically on hand.</param>
/// <param name="TotalReserved">SUM(reserved) — units held for pending orders.</param>
/// <param name="BelowThresholdCount">
/// Count of SKUs where <c>available &lt; threshold</c>. Sprint-6 ships
/// this as 0 — the <c>threshold</c> column lands in Sprint-7 alongside
/// the role/permission schema expansion.
/// </param>
/// <param name="OversellRiskCount">
/// Count of SKUs where <c>reserved &gt; available</c> — i.e. all current
/// reservations cannot be fulfilled if one more comes in. The
/// reservation ledger conditional-CTE INSERT prevents oversold rows at
/// write time, so this metric tracks SKUs in the danger zone, not
/// actually oversold ones.
/// </param>
public sealed record InventorySummaryDto(
    int TotalSkus,
    long TotalAvailable,
    long TotalReserved,
    int BelowThresholdCount,
    int OversellRiskCount);
