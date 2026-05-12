using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Audit row for one delta applied to <see cref="StockItem.Available"/>.
/// Persisted in <c>stock_adjustments</c> per Tech Design v3.0 §4.2 with
/// the originating <see cref="StockAdjustmentReason"/> so dashboards can
/// reconcile inbound receipts vs cycle-count corrections vs damage
/// write-offs without re-deriving from the wider event log.
/// </summary>
/// <remarks>
/// The aggregate root is <see cref="StockItem"/>; this entity is part of
/// the same consistency boundary. No standalone repository — adjustments
/// are written through <c>IStockItemRepository</c> alongside the
/// available-count update in one transaction.
/// </remarks>
public sealed class StockAdjustment : BaseEntity
{
    public Sku Sku { get; private set; } = default!;

    public int Delta { get; private set; }

    public StockAdjustmentReason Reason { get; private set; }

    public string? Note { get; private set; }

    private StockAdjustment() { }

    public static StockAdjustment Record(
        Sku sku,
        int delta,
        StockAdjustmentReason reason,
        string? note = null
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (delta == 0)
        {
            throw new ArgumentException("delta must be non-zero.", nameof(delta));
        }

        return new StockAdjustment
        {
            Sku = sku,
            Delta = delta,
            Reason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }
}
