using ShopFlow.Inventory.Domain.Events;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// StockItem aggregate root — the per-SKU on-hand and allocated counters.
/// Mirrors Tech Design §7.7 verbatim. Note that <c>AvailableQuantity</c> is
/// NOT stored on this aggregate: it is derived in the read model by joining
/// against active rows in <c>reservations_ledger</c>
/// (<see cref="Application.Ports.IStockItemRepository.GetAvailabilityAsync"/>).
/// </summary>
/// <remarks>
/// Mutating methods clamp at zero rather than throwing because the upstream
/// validation layer rejects negative inputs at construction (via
/// <see cref="Quantity"/>) or at the Application boundary; the clamp here
/// is a defence-in-depth invariant against a hypothetical handler bug, not
/// a substitute for input validation.
/// </remarks>
public sealed class StockItem : AggregateRoot
{
    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Category { get; private set; }

    public int TotalQuantity { get; private set; }

    public int AllocatedQuantity { get; private set; }

    public int SafetyThreshold { get; private set; }

    // EF Core constructor.
    private StockItem() { }

    public StockItem(
        Guid tenantId,
        Sku sku,
        string name,
        string? category,
        int totalQuantity,
        int safetyThreshold
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (totalQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalQuantity),
                totalQuantity,
                "Total quantity must be non-negative."
            );
        }

        if (safetyThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(safetyThreshold),
                safetyThreshold,
                "Safety threshold must be non-negative."
            );
        }

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Sku = sku.Value;
        Name = name;
        Category = category;
        TotalQuantity = totalQuantity;
        AllocatedQuantity = 0;
        SafetyThreshold = safetyThreshold;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Apply a positive or negative adjustment to <see cref="TotalQuantity"/>.
    /// Clamps at zero per Tech Design §7.7 — never goes negative even if the
    /// caller asks for a delta larger than the on-hand count. Raises
    /// <see cref="StockAdjustedEvent"/> with the carrier delta (the requested
    /// value, not the clamped delta) so downstream consumers can spot
    /// suspicious adjustments.
    /// </summary>
    public void AdjustStock(int delta, StockAdjustmentReason reason, Guid userId)
    {
        var newTotal = Math.Max(0, TotalQuantity + delta);
        TotalQuantity = newTotal;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(
            new StockAdjustedEvent(
                TenantId: TenantId,
                Sku: Sku,
                Delta: delta,
                NewTotalQuantity: newTotal,
                Reason: reason,
                UserId: userId,
                OccurredAt: DateTime.UtcNow
            )
        );
    }

    /// <summary>
    /// Confirm a previously-active reservation's quantity has shipped:
    /// deducts <paramref name="qty"/> from <see cref="TotalQuantity"/> and
    /// raises <see cref="StockChangedEvent"/>. The corresponding ledger row
    /// transition (Active → Confirmed) is performed by the repository in
    /// the same transaction; see <see cref="Application.Ports.IReservationRepository.ConfirmAsync"/>.
    /// </summary>
    public void ConfirmDeduction(int qty)
    {
        if (qty < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(qty),
                qty,
                "Confirm quantity must be non-negative."
            );
        }

        var newTotal = Math.Max(0, TotalQuantity - qty);
        TotalQuantity = newTotal;
        UpdatedAt = DateTime.UtcNow;

        // AvailableQuantity is derived against the live ledger; the event
        // carries the post-deduction total and a best-effort available =
        // total − allocated. The read-side projection that consumes this
        // event will re-query the ledger if a precise value is required.
        var approximateAvailable = Math.Max(0, newTotal - AllocatedQuantity);

        RaiseDomainEvent(
            new StockChangedEvent(
                TenantId: TenantId,
                Sku: Sku,
                NewTotalQuantity: newTotal,
                NewAvailableQuantity: approximateAvailable,
                OccurredAt: DateTime.UtcNow
            )
        );
    }
}
