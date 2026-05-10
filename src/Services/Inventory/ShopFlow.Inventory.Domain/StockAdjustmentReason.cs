namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Reason classification for a stock adjustment. Enumerated explicitly
/// (rather than free-text) so reporting and audit can group on a stable
/// vocabulary. Tech Design §7.7 references this as a typed parameter on
/// <see cref="StockItem.AdjustStock"/>.
/// </summary>
public enum StockAdjustmentReason
{
    /// <summary>Goods received against a PO or transfer.</summary>
    Receiving = 1,

    /// <summary>Cycle count or full stock-take reconciliation.</summary>
    StockTake = 2,

    /// <summary>Damaged in handling — write-off.</summary>
    Damaged = 3,

    /// <summary>Lost / unaccounted shrinkage.</summary>
    Lost = 4,

    /// <summary>Customer return restocked.</summary>
    Returned = 5,

    /// <summary>Catch-all; require a free-text note when used.</summary>
    Other = 6,
}
