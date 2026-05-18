using System.Collections.Concurrent;
using ShopFlow.Inventory.Application.Services;

namespace ShopFlow.Inventory.Infrastructure.Services;

/// <summary>
/// Process-memory implementation of <see cref="ISkuMetadataStore"/>.
///
/// Sprint-6 only — Sprint-7 replaces with EF-backed real columns on
/// <c>stock_items</c>. Stored as a nested ConcurrentDictionary so reads
/// + writes are lock-free; the outer key is the tenant slug and the
/// inner key is the SKU value. Singleton lifetime in DI.
/// </summary>
public sealed class InMemorySkuMetadataStore : ISkuMetadataStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SkuMetadata>> tenants = new();

    public int? GetThreshold(string tenantSlug, string sku) =>
        this.TryGet(tenantSlug, sku, out var m) ? m.Threshold : null;

    public void SetThreshold(string tenantSlug, string sku, int threshold)
    {
        var bag = this.tenants.GetOrAdd(tenantSlug, _ => new ConcurrentDictionary<string, SkuMetadata>());
        bag.AddOrUpdate(
            sku,
            _ => new SkuMetadata { Threshold = threshold },
            (_, existing) => existing with { Threshold = threshold });
    }

    public bool IsFlashSale(string tenantSlug, string sku) =>
        this.TryGet(tenantSlug, sku, out var m) && m.IsFlashSale;

    public void SetFlashSale(string tenantSlug, string sku, bool active)
    {
        var bag = this.tenants.GetOrAdd(tenantSlug, _ => new ConcurrentDictionary<string, SkuMetadata>());
        bag.AddOrUpdate(
            sku,
            _ => new SkuMetadata { IsFlashSale = active },
            (_, existing) => existing with { IsFlashSale = active });
    }

    private bool TryGet(string tenantSlug, string sku, out SkuMetadata metadata)
    {
        if (this.tenants.TryGetValue(tenantSlug, out var bag)
            && bag.TryGetValue(sku, out var found))
        {
            metadata = found;
            return true;
        }
        metadata = default!;
        return false;
    }

    private sealed record SkuMetadata
    {
        public int? Threshold { get; init; }
        public bool IsFlashSale { get; init; }
    }
}
