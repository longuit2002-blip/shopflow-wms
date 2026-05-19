using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.SharedKernel.Domain;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Read + write surface for the rich SKU catalog (Sprint-7.5 U3). Replaces
/// the in-memory <c>ISkuMetadataStore</c> singleton with a per-tenant
/// EF-backed table; the trade-off-closure release notes track the swap as
/// "cosmetic SKU schema expansion" (Sprint-6 trade-off #1).
/// </summary>
/// <remarks>
/// <para>Per AGENTS.md §3.16 every EF query passes through a tenant-scoped
/// repository — no raw <c>DbSet&lt;T&gt;</c> access in Application or Api
/// layers (ShopFlow0001 analyzer enforces). Implementations are constructed
/// with the per-request <see cref="InventoryDbContext"/> so the tenant DB
/// binding is read from <c>IRequestContext</c> at scope entry.</para>
///
/// <para>The <c>changed</c> flag on <see cref="UpsertAsync"/> and
/// <see cref="UpdateFlashSaleAsync"/> is the seam Sprint-7.5 U5 reads to
/// gate the <c>SkuFlashSaleChangedV1</c> outbox emit — idempotent
/// retries from the UI must not double-publish.</para>
/// </remarks>
public interface ISkuRepository
{
    /// <summary>
    /// Load the catalog row for <paramref name="code"/>; returns
    /// <c>null</c> when no row exists.
    /// </summary>
    Task<Sku?> GetByIdAsync(SkuCode code, CancellationToken ct);

    /// <summary>
    /// Insert when no row exists for <paramref name="sku"/>.Code; UPDATE
    /// the catalog metadata when one does. The returned tuple's
    /// <c>changed</c> flag is <c>true</c> on insert and on UPDATEs that
    /// changed at least one column; <c>false</c> when the caller's
    /// payload matches existing state. The returned aggregate is the
    /// post-write snapshot.
    /// </summary>
    Task<SkuMutationResult> UpsertAsync(Sku sku, CancellationToken ct);

    /// <summary>
    /// Page through the catalog, optionally filtering by a
    /// case-insensitive substring match on <c>Sku.Code</c> or
    /// <c>Sku.Name</c>. Ordering is by SKU code ascending for stable UI
    /// rendering. The returned <c>Total</c> is the unpaginated row count
    /// matching the filter.
    /// </summary>
    Task<(IReadOnlyList<Sku> Items, int Total)> ListPagedAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken ct
    );

    /// <summary>
    /// Toggle <c>is_flash_sale</c> on the catalog row. Caller passes the
    /// SKU code; the repository loads, mutates, and persists. Returns
    /// the post-update aggregate plus a <c>Changed</c> flag — Sprint-7.5
    /// U5 reads <c>Changed</c> to gate the <c>SkuFlashSaleChangedV1</c>
    /// outbox emit so retries from the UI do not double-publish.
    /// </summary>
    Task<Result<SkuMutationResult>> UpdateFlashSaleAsync(
        SkuCode code,
        bool active,
        CancellationToken ct
    );

    /// <summary>
    /// Set the low-stock threshold on the catalog row. When no row
    /// exists for <paramref name="code"/> the repository creates a
    /// minimal one (name defaults to the SKU code) so single-purpose
    /// callers like the Sprint-6 threshold-inline-edit do not also
    /// have to go through the Create SKU modal. The returned
    /// <see cref="SkuMutationResult.Changed"/> flag matches the upsert
    /// semantics on <see cref="UpsertAsync"/> — <c>true</c> on insert,
    /// <c>true</c> when the existing row's threshold differed,
    /// <c>false</c> when the existing row already held the requested
    /// value.
    /// </summary>
    Task<Result<SkuMutationResult>> UpdateThresholdAsync(
        SkuCode code,
        int? threshold,
        CancellationToken ct
    );

    /// <summary>
    /// Convenience read for the metadata reader path consumed by
    /// <c>ListSkusQueryHandler</c> + <c>GetInventorySummaryQueryHandler</c>
    /// — returns the threshold for one SKU, or <c>null</c> when the
    /// catalog row is missing or threshold is unset.
    /// </summary>
    Task<int?> GetThresholdAsync(SkuCode code, CancellationToken ct);

    /// <summary>
    /// Convenience read for the metadata reader path — returns
    /// <c>is_flash_sale</c>, or <c>false</c> when the catalog row is
    /// missing.
    /// </summary>
    Task<bool> IsFlashSaleAsync(SkuCode code, CancellationToken ct);

    /// <summary>
    /// Bulk read of catalog metadata used by the list / summary query
    /// handlers to avoid N+1 round-trips. Returns a per-SKU snapshot
    /// of <c>threshold</c> + <c>is_flash_sale</c> + <c>name</c> +
    /// <c>category</c> for the SKUs the caller passes in. SKUs without
    /// a <c>skus</c> row are omitted from the dictionary; callers
    /// treat absence as "threshold unset, is_flash_sale = false".
    /// </summary>
    Task<IReadOnlyDictionary<string, SkuListMetadataDto>> GetListMetadataAsync(
        IReadOnlyCollection<string> skuCodes,
        CancellationToken ct
    );

    /// <summary>
    /// Aggregate count for the summary query handler's
    /// <c>BelowThresholdCount</c> calculation. Returns a dictionary
    /// mapping SKU code → threshold for all rows where threshold is
    /// non-null. Callers compare against <c>stock_items.available</c>
    /// in memory.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetAllThresholdsAsync(CancellationToken ct);
}

/// <summary>
/// Per-SKU metadata snapshot for the list / summary query handlers.
/// Sprint-7.5 U3 — replaces the per-row in-memory <c>ISkuMetadataReader</c>
/// lookups with one bulk dictionary read.
/// </summary>
public sealed record SkuListMetadataDto(
    string Sku,
    string Name,
    string? Category,
    int? Threshold,
    bool IsFlashSale
);

/// <summary>
/// Return shape for write-path repository methods that need to surface
/// the post-write aggregate plus a "did anything change" flag. The flag
/// is the seam Sprint-7.5 U5 reads on <c>UpdateFlashSaleAsync</c> to
/// gate the <c>SkuFlashSaleChangedV1</c> outbox emit so idempotent
/// retries from the UI do not double-publish.
/// </summary>
/// <remarks>
/// A dedicated record (rather than a value tuple) keeps
/// <c>Result&lt;SkuMutationResult&gt;</c> ergonomic — <c>result.Value!.Changed</c>
/// works without the double-nullable-lift required for
/// <c>Result&lt;(Sku, bool)&gt;</c> on a value-type tuple.
/// </remarks>
public sealed record SkuMutationResult(Sku Sku, bool Changed);
}
