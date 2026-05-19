using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Catalog.ValueObjects;

/// <summary>
/// Physical dimensions of a SKU's primary package — length / width / height
/// expressed in a single unit. Used by put-away suggestion ranking, channel
/// shipping-fee calculation, and the rich-catalog edit modal landed in
/// Sprint-7.5 U4. All four components are required when the value is
/// supplied; if any dimension is unknown the caller stores
/// <c>null</c> on the parent <see cref="Sku"/> instead of a partial
/// value object.
/// </summary>
/// <remarks>
/// Persisted as <c>jsonb</c> on the <c>skus</c> table per Sprint-7.5 U3 —
/// the column is nullable, so SKUs without measured dimensions skip the
/// jsonb write entirely. Equality is structural over the four components.
/// </remarks>
public sealed class SkuDimensions : ValueObject
{
    public const int MaxUnitLength = 8;

    public decimal Length { get; }

    public decimal Width { get; }

    public decimal Height { get; }

    /// <summary>
    /// Short unit string (e.g. <c>"cm"</c>, <c>"mm"</c>, <c>"in"</c>). Free
    /// form within <see cref="MaxUnitLength"/> characters — the channel
    /// adapter layer handles unit conversion on the way to each
    /// marketplace API.
    /// </summary>
    public string Unit { get; }

    private SkuDimensions(decimal length, decimal width, decimal height, string unit)
    {
        Length = length;
        Width = width;
        Height = height;
        Unit = unit;
    }

    public static SkuDimensions Create(decimal length, decimal width, decimal height, string unit)
    {
        if (length <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "length must be > 0."
            );
        }
        if (width <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "width must be > 0."
            );
        }
        if (height <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "height must be > 0."
            );
        }
        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("unit is required.", nameof(unit));
        }
        var trimmed = unit.Trim();
        if (trimmed.Length > MaxUnitLength)
        {
            throw new ArgumentException(
                $"unit must be {MaxUnitLength} characters or fewer; got {trimmed.Length}.",
                nameof(unit)
            );
        }

        return new SkuDimensions(length, width, height, trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Length;
        yield return Width;
        yield return Height;
        yield return Unit;
    }
}
