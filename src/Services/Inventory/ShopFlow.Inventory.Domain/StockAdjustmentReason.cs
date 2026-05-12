namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Why the stock-on-hand for a SKU changed. Recorded against each
/// <c>stock_adjustments</c> row per Tech Design v3.0 §4.2 so the audit
/// trail can distinguish marketplace inflows from cycle-count corrections
/// from damage write-offs.
/// </summary>
/// <remarks>
/// Persisted as the string name (not the ordinal) — EF Core conversion in
/// the entity configuration. Adding a new reason is a backwards-compatible
/// migration (add the column value; existing rows untouched).
/// </remarks>
public enum StockAdjustmentReason
{
    /// <summary>Operator-driven manual correction (rare; should be audited).</summary>
    Manual = 0,

    /// <summary>Receipt of inbound shipment.</summary>
    Receipt = 1,

    /// <summary>Cycle-count reconciliation against the physical pick-face.</summary>
    CycleCount = 2,

    /// <summary>Damaged unit written off.</summary>
    Damage = 3,

    /// <summary>Lost or unaccounted unit (theft, shrink).</summary>
    Loss = 4,

    /// <summary>Return restocked to available inventory.</summary>
    ReturnRestock = 5,
}
