using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// SKU value object — a non-empty, length-bounded string identifier for
/// a stock-keeping unit. Equality is structural per <see cref="ValueObject"/>.
/// Length cap of 64 mirrors the column width in the <c>stock_items</c>
/// migration (Tech Design §7.2).
/// </summary>
public sealed class Sku : ValueObject
{
    public const int MaxLength = 64;

    public string Value { get; }

    public Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "SKU must be a non-empty, non-whitespace string.",
                nameof(value)
            );
        }

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"SKU length {value.Length} exceeds the maximum of {MaxLength}.",
                nameof(value)
            );
        }

        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
