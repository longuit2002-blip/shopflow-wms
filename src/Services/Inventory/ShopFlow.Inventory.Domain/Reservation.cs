namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Immutable projection of a row in <c>reservations_ledger</c>. Per Tech
/// Design §7.2 reservations are append-only; lifecycle transitions are
/// expressed as new <see cref="Status"/> values written via UPDATE, never as
/// row deletions.
/// </summary>
/// <remarks>
/// <para>
/// This is a record because the EF entity configuration in
/// <c>Infrastructure/EntityConfigurations/ReservationConfiguration.cs</c>
/// configures it without inheriting <see cref="SharedKernel.Domain.BaseEntity"/>:
/// the row already carries <see cref="TenantId"/> + <see cref="Id"/> directly,
/// and we do not raise domain events on a Reservation (the events are
/// raised on <see cref="StockItem"/> and at the repository boundary). EF
/// Core 8 supports records with init-only properties as entities.
/// </para>
/// <para>
/// <see cref="IsActive"/> takes an explicit <c>nowUtc</c> parameter so the
/// caller passes an injected clock value (per AGENTS.md §5.31 — no
/// implicit <c>DateTime.UtcNow</c> reads in domain logic that influences
/// behaviour).
/// </para>
/// </remarks>
public sealed record Reservation(
    Guid Id,
    Guid TenantId,
    string Sku,
    int Qty,
    Guid OrderId,
    ReservationStatus Status,
    DateTime ReservedAt,
    DateTime ExpiresAt,
    DateTime? FinalizedAt
)
{
    /// <summary>
    /// True iff the reservation is still counted against available stock:
    /// status is <see cref="ReservationStatus.Active"/> AND the expiry
    /// instant has not yet passed (per the ledger's expiry worker —
    /// Tech Design §7.4).
    /// </summary>
    /// <param name="nowUtc">
    /// Current UTC instant, supplied by the caller from an injected
    /// <see cref="TimeProvider"/> or an integration-test fixed clock.
    /// </param>
    public bool IsActive(DateTime nowUtc) =>
        Status == ReservationStatus.Active && ExpiresAt > nowUtc;
}
