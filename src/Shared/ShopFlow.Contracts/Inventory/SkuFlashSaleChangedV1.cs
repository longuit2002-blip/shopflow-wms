namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Cross-module event signaling that a SKU's <c>is_flash_sale</c> flag
/// flipped inside a tenant DB. Sprint-7.5 U5 — closes Sprint-6 trade-off
/// #10 (flash-sale dual-write). Inventory emits on every UPDATE that
/// changes state (no emit on no-op writes); StockSync's
/// <c>SkuFlashSaleChangedConsumer</c> upserts the matching
/// <c>sku_flags</c> row idempotently (existing Sprint-5 U7 KTD7
/// UNIQUE-23505 pattern + an OccurredAt-vs-stored guard added in U5
/// — see KTD3 — so stale writes are rejected under consumer
/// parallelism that arrives once the W6 split lands).
/// </summary>
/// <param name="TenantId">Tenant scope (catalog DB id).</param>
/// <param name="Sku">SKU code (matches <c>skus.sku</c> + <c>sku_flags.sku</c>).</param>
/// <param name="IsFlashSale">New flag state (true = flash-sale).</param>
/// <param name="OccurredAt">UTC timestamp of the UPDATE. Used by the
/// consumer's OccurredAt guard to reject stale writes when the
/// MultiplexedOutboxDispatcher's per-tenant-only FIFO doesn't preserve
/// per-(tenant, sku) ordering.</param>
public sealed record SkuFlashSaleChangedV1(
    Guid TenantId,
    string Sku,
    bool IsFlashSale,
    DateTime OccurredAt
);
