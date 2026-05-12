using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Stock-keeping unit identifier — the natural key of <see cref="StockItem"/>
/// and the foreign-key bridge from <see cref="Reservation"/> rows. Per Tech
/// Design v3.0 §4.2 SKU is a free-form short string scoped to the tenant
/// database; uniqueness within the cluster is implicit (the DB identity is
/// the tenant boundary, AGENTS.md §3.14).
/// </summary>
/// <remarks>
/// Validation: non-empty, &lt;= 64 characters, trimmed. Casing is preserved
/// because some marketplaces ship case-sensitive SKUs (Shopee variant
/// suffixes); the equality contract is therefore case-sensitive. Use
/// <see cref="StockItem"/>'s repository for case-folded lookups if the
/// caller needs that behavior.
/// </remarks>
public sealed class Sku : ValueObject
{
    public const int MaxLength = 64;

    public string Value { get; }

    private Sku(string value)
    {
        Value = value;
    }

    public static Sku Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("sku is required.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException(
                $"sku must be {MaxLength} characters or fewer; got {trimmed.Length}.",
                nameof(value)
            );
        }

        return new Sku(trimmed);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
