using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.StockSync.Domain.Aggregates;

/// <summary>
/// Per-SKU flag carrying the <c>is_flash_sale</c> bit consumed by the
/// StockSync engine's priority queue routing (Sprint-5 plan R10/U7).
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 (DB-per-tenant) no <c>tenant_id</c> column — the
/// database identity is the tenant boundary. The primary key is the
/// <c>Sku</c> string itself, mirroring Sprint-1-redux <c>StockItem</c>: the
/// inherited <see cref="BaseEntity.Id"/> Guid is ignored in EF mapping
/// (HasKey points at Sku).</para>
/// <para>Lifecycle is tiny: created/updated by the admin
/// <c>PUT /api/skus/{sku}/flag</c> endpoint (Sprint-5 U7). No domain
/// events — the engine reads via <c>ISkuFlagRepository</c> at flush time
/// and routes accordingly.</para>
/// </remarks>
public sealed class SkuFlag : BaseEntity
{
    public string Sku { get; private set; } = default!;

    public bool IsFlashSale { get; private set; }

    private SkuFlag() { }

    public static SkuFlag Create(string sku, bool isFlashSale)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("SKU must be non-empty", nameof(sku));
        }

        return new SkuFlag
        {
            Sku = sku,
            IsFlashSale = isFlashSale,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Idempotent setter — when the requested value equals the current
    /// value, the row is not touched (UpdatedAt unchanged). Otherwise the
    /// flag flips and UpdatedAt advances.
    /// </summary>
    public void SetFlashSale(bool isFlashSale)
    {
        if (IsFlashSale == isFlashSale)
        {
            return;
        }

        IsFlashSale = isFlashSale;
        UpdatedAt = DateTime.UtcNow;
    }
}
