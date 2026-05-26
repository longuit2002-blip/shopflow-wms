namespace ShopFlow.StockSync.Application.Ports;

/// <summary>
/// Application-layer port the StockSync consumer + admin endpoint consume
/// to read/write the <c>sku_flag</c> table (Sprint-5 plan U7).
/// </summary>
/// <remarks>
/// <para>Sprint-5 plan U7 amendment: every method takes an explicit
/// <c>tenantId</c>. The DB-backed implementation lives behind a scoped
/// <see cref="ShopFlow.StockSync.Infrastructure.StockSyncDbContext"/>
/// bound to the per-tenant connection string via the K12 pattern, so
/// the parameter is informational at the inner layer. The caching
/// decorator (also U7) keys its in-memory cache by
/// <c>(tenantId, sku)</c> and uses <c>tenantId</c> to open a tenant-
/// bound DI scope when the cache misses — that's why the port carries
/// the tenant id rather than relying on an ambient
/// <see cref="ShopFlow.SharedKernel.Application.IRequestContext"/>.</para>
///
/// <para>The consumer (<c>StockLevelChangedConsumer</c>) runs without
/// an active request scope; it passes
/// <see cref="ShopFlow.Contracts.Inventory.StockLevelChangedV1.TenantId"/>
/// through. The admin controller is request-scoped and pulls the same
/// id from <see cref="ShopFlow.SharedKernel.Application.IRequestContext.TenantId"/>.</para>
/// </remarks>
public interface ISkuFlagRepository
{
    /// <summary>
    /// Returns <c>true</c> when an explicit flash-sale flag exists for
    /// (<paramref name="tenantId"/>, <paramref name="sku"/>) and is set;
    /// <c>false</c> when the row is absent or the flag is cleared.
    /// </summary>
    Task<bool> IsFlashSaleAsync(Guid tenantId, string sku, CancellationToken ct);

    /// <summary>
    /// Idempotent upsert called by the admin
    /// <c>PUT /api/skus/{sku}/flag</c> endpoint (Sprint-5 U7).
    /// Implementations must catch UNIQUE-23505 and fall back to UPDATE
    /// so concurrent admin writes don't surface as 500s.
    /// </summary>
    Task SetFlashSaleAsync(Guid tenantId, string sku, bool isFlashSale, CancellationToken ct);

    /// <summary>
    /// Sprint-7.5 U5 — consumer entry point for <c>SkuFlashSaleChangedV1</c>
    /// events from Inventory. Combines the UNIQUE-23505 idempotent upsert
    /// with an OccurredAt-vs-stored guard: when the existing row's
    /// <c>UpdatedAt</c> (or <c>CreatedAt</c> when never updated) is newer
    /// than <paramref name="occurredAt"/>, the call is a no-op (stale write
    /// rejected). The guard is independent of dispatcher partitioning so
    /// flap A→B→A converges on the final intended state even under W6
    /// competing consumers when per-(tenant, sku) FIFO can't be preserved.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the row was inserted or updated; <c>false</c> if the
    /// write was rejected as stale (older OccurredAt than the stored row).
    /// </returns>
    Task<bool> ApplyEventAsync(
        Guid tenantId,
        string sku,
        bool isFlashSale,
        DateTime occurredAt,
        CancellationToken ct
    );
}
