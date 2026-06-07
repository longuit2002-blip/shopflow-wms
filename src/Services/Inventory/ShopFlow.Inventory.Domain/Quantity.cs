using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Non-negative integer count of physical units. Wraps the raw <see cref="int"/>
/// so arithmetic on quantities is explicit (oversell vs underflow is a
/// correctness invariant per AGENTS.md §0 "correctness over latency"), and
/// to make the Domain layer fail fast on a negative quantity rather than
/// pushing the check into every caller.
/// </summary>
/// <remarks>
/// Arithmetic helpers (<see cref="Add"/>, <see cref="Subtract"/>) raise
/// <see cref="InvalidOperationException"/> on underflow; the application
/// layer wraps these in <see cref="Result{T}"/> when the underflow is an
/// expected business outcome (oversold). U8 ships the type and arithmetic;
/// the reservation flows that consume it land in Sprint-1-redux.
/// </remarks>
public sealed class Quantity : ValueObject
{
    public int Value { get; }

    public static readonly Quantity Zero = new(0);

    private Quantity(int value)
    {
        Value = value;
    }

    public static Quantity From(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "quantity must be non-negative."
            );
        }

        return new Quantity(value);
    }

    public Quantity Add(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return From(checked(Value + other.Value));
    }

    public Quantity Subtract(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Value < other.Value)
        {
            throw new InvalidOperationException(
                $"quantity underflow: {Value} - {other.Value} < 0."
            );
        }
        return From(Value - other.Value);
    }

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
