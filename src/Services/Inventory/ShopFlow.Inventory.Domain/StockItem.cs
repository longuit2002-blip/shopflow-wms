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
    /// Reserve <paramref name="quantity"/> units. Sprint-1-redux implements
    /// the ledger-conditional-INSERT pattern (Tech Design v3.0 §4.4) which
    /// makes the actual decision; this aggregate method is the API the
    /// repository wraps. U8 throws so behavior tests stay red until the
    /// ledger arrives.
    /// </summary>
    public Result Reserve(Quantity quantity)
    {
        _ = quantity;
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    /// <summary>
    /// Confirm a previously pending reservation (move <c>quantity</c> from
    /// Reserved to gone). Raises <see cref="StockReservedEvent"/> for
    /// downstream consumers. Sprint-1-redux behavior.
    /// </summary>
    public Result Confirm(Quantity quantity)
    {
        _ = quantity;
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    /// <summary>
    /// Release a pending reservation back to Available. Sprint-1-redux behavior.
    /// </summary>
    public Result Release(Quantity quantity)
    {
        _ = quantity;
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    /// <summary>
    /// Apply a stock adjustment (receipt, damage, cycle-count). The
    /// reservation ledger is not involved; available and the adjustment
    /// row are written in one transaction. Sprint-1-redux behavior.
    /// </summary>
    public Result Adjust(int delta, StockAdjustmentReason reason)
    {
        _ = (delta, reason);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }
}
