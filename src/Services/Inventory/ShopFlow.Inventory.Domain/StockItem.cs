using ShopFlow.Inventory.Domain.Events;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Aggregate root tracking the available-and-reserved counts for one SKU
/// inside a tenant DB. Per Tech Design v3.0 §4.2 the primary key is the
/// SKU itself (not a surrogate Guid), and the row carries no
/// <c>tenant_id</c> — the database identity is the tenant boundary per
/// ADR-0003.
/// </summary>
/// <remarks>
/// <para>U8 ships the type shape, the domain-event buffer, and a behavior
/// surface whose bodies <see cref="NotImplementedException"/> until
/// Sprint-1-redux (plan 003). The append-only reservation ledger pattern
/// described in Tech Design v3.0 §4.4 is what actually mutates available /
/// reserved; this aggregate's stock counts are a materialised projection
/// of the ledger plus the adjustment history, kept in sync inside the
/// same transaction as the reservation write.</para>
///
/// <para>RowVersion uses Postgres' <c>xid</c> with default
/// <c>(txid_current())::text::xid</c> per Tech Design v3.0 §4.2 — matches
/// the control-plane <c>tenants.row_version</c> pattern. EF Core treats
/// it as the optimistic-concurrency token; conflicts surface as
/// <c>DbUpdateConcurrencyException</c>, mapped to a domain
/// <c>Result.Failure("stock.concurrency_conflict")</c> in the application
/// layer.</para>
///
/// <para>Inherits from <see cref="BaseEntity"/> rather than
/// <see cref="AggregateRoot"/> because the inherited <c>byte[] RowVersion</c>
/// on <see cref="AggregateRoot"/> doesn't match the Postgres <c>xid</c>
/// shape; <c>StockItem</c> declares its own <c>uint RowVersion</c>. The
/// inherited Guid <c>Id</c> from <see cref="BaseEntity"/> is ignored in
/// the EF mapping (HasKey points at Sku.Value). The domain-event buffer
/// + CreatedAt/UpdatedAt from <see cref="BaseEntity"/> survive.</para>
/// </remarks>
public sealed class StockItem : BaseEntity
{
    public Sku Sku { get; private set; } = default!;

    public Quantity Available { get; private set; } = Quantity.Zero;

    public Quantity Reserved { get; private set; } = Quantity.Zero;

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Optional FK to <see cref="Zone.ZoneId"/>. When set, the put-away
    /// suggestion service ranks bins in this zone first. Sprint-2-redux
    /// plan R13; settable via <see cref="SetHomeZone"/>.
    /// </summary>
    public long? HomeZoneId { get; private set; }

    private StockItem() { }

    public static StockItem Create(Sku sku, Quantity initialAvailable)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(initialAvailable);

        return new StockItem
        {
            Sku = sku,
            Available = initialAvailable,
            Reserved = Quantity.Zero,
        };
    }

    /// <summary>
    /// Assign or change the SKU's home zone for put-away ranking purposes.
    /// </summary>
    public void SetHomeZone(long? zoneId)
    {
        HomeZoneId = zoneId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reserve <paramref name="quantity"/> units — moves them from
    /// <see cref="Available"/> into <see cref="Reserved"/>. The hot-path
    /// repository (<c>ReservationRepository.TryReserveAsync</c>) implements
    /// the ledger-conditional-INSERT pattern in raw SQL for atomicity under
    /// the flash-sale hot-key race; this aggregate method exists for
    /// non-hot-path callers (admin tools, replays) where round-trip cost is
    /// not a concern.
    /// </summary>
    public Result Reserve(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Value == 0)
        {
            return Result.Failure("quantity must be > 0.", "stock.quantity_zero");
        }
        if (Available.Value < quantity.Value)
        {
            return Result.Failure(
                $"insufficient stock: available={Available.Value}, requested={quantity.Value}.",
                "stock.insufficient"
            );
        }

        Available = Available.Subtract(quantity);
        Reserved = Reserved.Add(quantity);
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Confirm a previously pending reservation — units physically leave the
    /// warehouse. <see cref="Reserved"/> decreases; <see cref="Available"/>
    /// is unchanged (the units were already excluded from Available at
    /// reserve-time).
    /// </summary>
    public Result Confirm(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Value == 0)
        {
            return Result.Failure("quantity must be > 0.", "stock.quantity_zero");
        }
        if (Reserved.Value < quantity.Value)
        {
            return Result.Failure(
                $"reserved underflow: reserved={Reserved.Value}, confirming={quantity.Value}.",
                "stock.reserved_underflow"
            );
        }

        Reserved = Reserved.Subtract(quantity);
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(
            new StockChangedEvent(Sku.Value, Available.Value, Reserved.Value, UpdatedAt!.Value)
        );
        return Result.Success();
    }

    /// <summary>
    /// Release a pending reservation back to <see cref="Available"/> — units
    /// stay in the warehouse, just leave the reservation hold.
    /// </summary>
    public Result Release(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Value == 0)
        {
            return Result.Failure("quantity must be > 0.", "stock.quantity_zero");
        }
        if (Reserved.Value < quantity.Value)
        {
            return Result.Failure(
                $"reserved underflow: reserved={Reserved.Value}, releasing={quantity.Value}.",
                "stock.reserved_underflow"
            );
        }

        Reserved = Reserved.Subtract(quantity);
        Available = Available.Add(quantity);
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Apply a stock adjustment (receipt, damage, cycle-count). Positive
    /// <paramref name="delta"/> increases <see cref="Available"/>; negative
    /// decreases. The reservation ledger is not involved — caller persists
    /// a <see cref="StockAdjustment"/> audit row in the same transaction.
    /// </summary>
    public Result Adjust(int delta, StockAdjustmentReason reason)
    {
        _ = reason;
        if (delta == 0)
        {
            return Result.Failure("delta must be non-zero.", "stock.adjustment_zero");
        }
        if (delta < 0)
        {
            var dec = Quantity.From(-delta);
            if (Available.Value < dec.Value)
            {
                return Result.Failure(
                    $"adjustment underflow: available={Available.Value}, delta={delta}.",
                    "stock.adjustment_underflow"
                );
            }
            Available = Available.Subtract(dec);
        }
        else
        {
            Available = Available.Add(Quantity.From(delta));
        }
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
