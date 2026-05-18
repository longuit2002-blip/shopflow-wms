using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.Application.Services;

/// <summary>
/// In-memory per-tenant store for cosmetic SKU metadata that Sprint-6
/// surfaces in the UI but doesn't yet have a schema column for:
///   - threshold (R9 — low-stock alert level)
///   - is_flash_sale (R10 — Channel module flag)
///
/// Sprint-7 swaps this for real <c>stock_items.threshold</c> +
/// <c>stock_items.is_flash_sale</c> columns. The interface stays so
/// command handlers don't change.
///
/// Persistence is process-memory only — values reset on Inventory.Api
/// restart. Acceptable for the demo loop; documented as a deferral.
/// </summary>
public interface ISkuMetadataStore
{
    /// <summary>Threshold for the SKU; null if unset.</summary>
    int? GetThreshold(string tenantSlug, string sku);

    void SetThreshold(string tenantSlug, string sku, int threshold);

    bool IsFlashSale(string tenantSlug, string sku);

    void SetFlashSale(string tenantSlug, string sku, bool active);
}

/// <summary>
/// Used by query handlers to attach metadata onto the SKU list response.
/// </summary>
public interface ISkuMetadataReader
{
    int? GetThreshold(string sku);
    bool IsFlashSale(string sku);
}

/// <summary>
/// Adapter so query handlers can ask for metadata "for the current
/// tenant" without re-passing the tenant slug. Pulls
/// <see cref="IRequestContext.TenantSlug"/> from the request scope.
/// </summary>
public sealed class TenantScopedSkuMetadataReader(
    ISkuMetadataStore store,
    IRequestContext requestContext) : ISkuMetadataReader
{
    private readonly ISkuMetadataStore store = store;
    private readonly IRequestContext requestContext = requestContext;

    public int? GetThreshold(string sku) =>
        this.store.GetThreshold(this.requestContext.TenantSlug, sku);

    public bool IsFlashSale(string sku) =>
        this.store.IsFlashSale(this.requestContext.TenantSlug, sku);
}
