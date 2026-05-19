using ShopFlow.Inventory.Domain.Catalog.ValueObjects;
using ShopFlow.SharedKernel.Domain;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Domain.Catalog;

/// <summary>
/// Rich SKU catalog aggregate — landed in Sprint-7.5 U3 to replace the
/// in-memory <c>ISkuMetadataStore</c> singleton with a real per-tenant
/// table. Holds the cosmetic + classification metadata the Inventory
/// screen surfaces (name, category, threshold, weight, dimensions,
/// description, image, barcode, brand) plus the load-bearing
/// <c>is_flash_sale</c> flag that StockSync's channel push tier reads
/// to bypass the per-(tenant, channel) coalescing window (Sprint-5 R10).
/// </summary>
/// <remarks>
/// <para><strong>Type name collision note (Sprint-7.5 U3 KTD).</strong>
/// The Inventory module already exports a <c>ShopFlow.Inventory.Domain.Sku</c>
/// value object — a 64-char string wrapper used as the natural key on
/// <see cref="StockItem"/> and <see cref="Reservation"/>. The blast
/// radius of renaming that value object across StockItem / Reservation /
/// EF configurations / repositories / channel adapters is large; the
/// new aggregate keeps the bare name <c>Sku</c> and lives in the
/// <c>ShopFlow.Inventory.Domain.Catalog</c> sub-namespace. Inside this
/// file the legacy value object is aliased to <c>SkuCode</c> via
/// <c>using SkuCode = ShopFlow.Inventory.Domain.Sku;</c> so the two are
/// distinguishable at every call site.</para>
///
/// <para><strong>Persistence (Sprint-7.5 U3 KTD2).</strong> Mapped to
/// the <c>skus</c> table via
/// <c>Infrastructure.EntityConfigurations.SkuConfiguration</c>. The PK
/// is the SKU string itself (<see cref="Code"/>); the inherited
/// <c>BaseEntity.Id</c> Guid is ignored. <see cref="IsFlashSale"/> is
/// projected to its own column for partial-index optimisation
/// (<c>ix_skus_is_flash_sale</c> WHERE is_flash_sale = TRUE). The 10
/// columns match the Sprint-7.5 brainstorm R1 contract.</para>
///
/// <para><strong>U5 seam.</strong> <see cref="UpdateFlashSale"/> returns
/// a boolean <c>changed</c> flag — together with the repository's
/// <c>UpdateFlashSaleAsync</c> tuple shape this lets the Sprint-7.5 U5
/// outbox emit short-circuit on no-op writes (idempotent caller path
/// won't double-publish <c>SkuFlashSaleChangedV1</c>). U3 leaves the
/// emit seam unwired; U5 fills it in.</para>
/// </remarks>
public sealed class Sku : BaseEntity
{
    /// <summary>Maximum length for free-text columns (name, brand, etc.).</summary>
    public const int MaxNameLength = 256;
    public const int MaxCategoryLength = 128;
    public const int MaxDescriptionLength = 2048;
    public const int MaxImageUrlLength = 1024;
    public const int MaxBarcodeLength = 64;
    public const int MaxBrandLength = 128;

    /// <summary>
    /// SKU string identifier — natural key. Stable across renames of
    /// <see cref="Name"/>. Wraps the same domain value object used by
    /// <see cref="StockItem"/> so the legacy <c>stock_items.sku</c> FK
    /// remains a string match against this column.
    /// </summary>
    public SkuCode Code { get; private set; } = default!;

    public string Name { get; private set; } = string.Empty;

    public string? Category { get; private set; }

    /// <summary>
    /// Low-stock alert threshold (R9). The Inventory screen highlights
    /// rows where <c>stock_items.available &lt; threshold</c>. Null
    /// means "not set" — disables the alert for this SKU.
    /// </summary>
    public int? Threshold { get; private set; }

    /// <summary>
    /// Single-unit weight in grams. Null when not measured. Channel
    /// adapters use this for shipping-fee calculation; per
    /// AGENTS.md §0 (correctness over latency) callers fail closed when
    /// the value is missing instead of guessing.
    /// </summary>
    public int? WeightGrams { get; private set; }

    /// <summary>Physical dimensions; null when not measured.</summary>
    public SkuDimensions? Dimensions { get; private set; }

    public string? Description { get; private set; }

    public string? ImageUrl { get; private set; }

    /// <summary>
    /// Optional product barcode (EAN/UPC). The DB-level partial UNIQUE
    /// index <c>ux_skus_barcode WHERE barcode IS NOT NULL</c> enforces
    /// uniqueness only for non-null values, matching the catalog
    /// reality where some SKUs ship without a printed barcode.
    /// </summary>
    public string? Barcode { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>
    /// Flash-sale flag (R10) consumed by StockSync's
    /// <c>StockLevelChangedConsumer</c> to bypass the per-channel
    /// coalescing window. UPDATEs through
    /// <see cref="UpdateFlashSale"/> so the U5 outbox emit can short-
    /// circuit on no-op writes.
    /// </summary>
    public bool IsFlashSale { get; private set; }

    private Sku() { }

    /// <summary>
    /// Factory for a new SKU catalog row. Validation only — persistence
    /// + uniqueness-on-barcode happen at the repository layer.
    /// </summary>
    public static Result<Sku> Create(
        SkuCode code,
        string name,
        string? category = null,
        int? threshold = null,
        int? weightGrams = null,
        SkuDimensions? dimensions = null,
        string? description = null,
        string? imageUrl = null,
        string? barcode = null,
        string? brand = null,
        bool isFlashSale = false
    )
    {
        ArgumentNullException.ThrowIfNull(code);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Sku>.Failure(
                "sku name is required.",
                "sku.name_required"
            );
        }
        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return Result<Sku>.Failure(
                $"sku name must be {MaxNameLength} characters or fewer.",
                "sku.name_too_long"
            );
        }

        if (threshold is < 0)
        {
            return Result<Sku>.Failure(
                "threshold must be >= 0.",
                "sku.threshold_negative"
            );
        }
        if (weightGrams is < 0)
        {
            return Result<Sku>.Failure(
                "weight_grams must be >= 0.",
                "sku.weight_negative"
            );
        }

        var validationFailure = ValidateOptionalStrings(
            category,
            description,
            imageUrl,
            barcode,
            brand
        );
        if (validationFailure is not null)
        {
            return Result<Sku>.Failure(validationFailure.Value.error, validationFailure.Value.code);
        }

        var now = DateTime.UtcNow;
        return Result<Sku>.Success(
            new Sku
            {
                Code = code,
                Name = trimmedName,
                Category = Trim(category),
                Threshold = threshold,
                WeightGrams = weightGrams,
                Dimensions = dimensions,
                Description = Trim(description),
                ImageUrl = Trim(imageUrl),
                Barcode = Trim(barcode),
                Brand = Trim(brand),
                IsFlashSale = isFlashSale,
                CreatedAt = now,
            }
        );
    }

    /// <summary>
    /// Update the low-stock threshold. Sprint-6 plan U8 / R9 — was the
    /// in-memory store path; Sprint-7.5 U3 routes through the
    /// repository.
    /// </summary>
    public Result UpdateThreshold(int? threshold)
    {
        if (threshold is < 0)
        {
            return Result.Failure(
                "threshold must be >= 0.",
                "sku.threshold_negative"
            );
        }

        if (Threshold == threshold)
        {
            return Result.Success();
        }

        Threshold = threshold;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Toggle the flash-sale flag. Returns <c>true</c> when state
    /// actually changed; <c>false</c> when the toggle was a no-op
    /// (caller already at the requested value). U5 reads the return
    /// to gate the <c>SkuFlashSaleChangedV1</c> outbox emit so
    /// idempotent retries do not double-publish.
    /// </summary>
    public bool UpdateFlashSale(bool active)
    {
        if (IsFlashSale == active)
        {
            return false;
        }

        IsFlashSale = active;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Replace the catalog metadata fields. Used by Sprint-7.5 U4's
    /// EditSkuModal write path. Returns <c>true</c> when at least one
    /// field changed; <c>false</c> when the payload matches existing
    /// state.
    /// </summary>
    public Result<bool> UpdateMetadata(
        string name,
        string? category,
        int? threshold,
        int? weightGrams,
        SkuDimensions? dimensions,
        string? description,
        string? imageUrl,
        string? barcode,
        string? brand
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<bool>.Failure("sku name is required.", "sku.name_required");
        }
        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return Result<bool>.Failure(
                $"sku name must be {MaxNameLength} characters or fewer.",
                "sku.name_too_long"
            );
        }
        if (threshold is < 0)
        {
            return Result<bool>.Failure("threshold must be >= 0.", "sku.threshold_negative");
        }
        if (weightGrams is < 0)
        {
            return Result<bool>.Failure("weight_grams must be >= 0.", "sku.weight_negative");
        }

        var validationFailure = ValidateOptionalStrings(
            category,
            description,
            imageUrl,
            barcode,
            brand
        );
        if (validationFailure is not null)
        {
            return Result<bool>.Failure(
                validationFailure.Value.error,
                validationFailure.Value.code
            );
        }

        var trimmedCategory = Trim(category);
        var trimmedDescription = Trim(description);
        var trimmedImageUrl = Trim(imageUrl);
        var trimmedBarcode = Trim(barcode);
        var trimmedBrand = Trim(brand);

        var changed =
            Name != trimmedName
            || Category != trimmedCategory
            || Threshold != threshold
            || WeightGrams != weightGrams
            || !Equals(Dimensions, dimensions)
            || Description != trimmedDescription
            || ImageUrl != trimmedImageUrl
            || Barcode != trimmedBarcode
            || Brand != trimmedBrand;

        if (!changed)
        {
            return Result<bool>.Success(false);
        }

        Name = trimmedName;
        Category = trimmedCategory;
        Threshold = threshold;
        WeightGrams = weightGrams;
        Dimensions = dimensions;
        Description = trimmedDescription;
        ImageUrl = trimmedImageUrl;
        Barcode = trimmedBarcode;
        Brand = trimmedBrand;
        UpdatedAt = DateTime.UtcNow;
        return Result<bool>.Success(true);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (string error, string code)? ValidateOptionalStrings(
        string? category,
        string? description,
        string? imageUrl,
        string? barcode,
        string? brand
    )
    {
        if (category is not null && category.Trim().Length > MaxCategoryLength)
        {
            return (
                $"category must be {MaxCategoryLength} characters or fewer.",
                "sku.category_too_long"
            );
        }
        if (description is not null && description.Trim().Length > MaxDescriptionLength)
        {
            return (
                $"description must be {MaxDescriptionLength} characters or fewer.",
                "sku.description_too_long"
            );
        }
        if (imageUrl is not null && imageUrl.Trim().Length > MaxImageUrlLength)
        {
            return (
                $"image_url must be {MaxImageUrlLength} characters or fewer.",
                "sku.image_url_too_long"
            );
        }
        if (barcode is not null && barcode.Trim().Length > MaxBarcodeLength)
        {
            return (
                $"barcode must be {MaxBarcodeLength} characters or fewer.",
                "sku.barcode_too_long"
            );
        }
        if (brand is not null && brand.Trim().Length > MaxBrandLength)
        {
            return (
                $"brand must be {MaxBrandLength} characters or fewer.",
                "sku.brand_too_long"
            );
        }
        return null;
    }
}
