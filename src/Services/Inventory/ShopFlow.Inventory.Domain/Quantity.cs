using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Non-negative integer quantity. Wrapping <see cref="int"/> in a value
/// object lets the type system signal "this is a quantity, not just any
/// number" at API and domain boundaries. Construction rejects negative
/// values; arithmetic that would push a quantity below zero is the caller's
/// responsibility (see <see cref="StockItem.AdjustStock"/> which clamps).
/// </summary>
public sealed class Quantity : ValueObject
{
    public int Value { get; }

    public Quantity(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Quantity must be non-negative."
            );
        }

        Value = value;
    }

    public static Quantity Zero { get; } = new(0);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}
