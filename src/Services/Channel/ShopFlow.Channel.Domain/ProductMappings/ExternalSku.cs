using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Domain.ProductMappings;

/// <summary>
/// Marketplace-side SKU identifier — the external half of a
/// <see cref="ProductMapping"/> row per Sprint-4 plan U1. Equality is
/// case-insensitive ordinal: marketplace dashboards routinely round-trip a
/// SKU through case-folding tools and operators paste "SKU-001" /
/// "sku-001" / "Sku-001" interchangeably. The stored canonical form
/// preserves the operator's original casing for display but compares
/// case-insensitively for lookup.
/// </summary>
public sealed class ExternalSku : ValueObject
{
    public const int MaxLength = 128;

    public string Value { get; }

    private ExternalSku(string value)
    {
        Value = value;
    }

    public static Result<ExternalSku> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ExternalSku>.Failure(
                "external_sku is required.",
                "mapping.external_sku_required"
            );
        }
        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            return Result<ExternalSku>.Failure(
                $"external_sku must be {MaxLength} characters or fewer; got {trimmed.Length}.",
                "mapping.external_sku_too_long"
            );
        }
        return Result<ExternalSku>.Success(new ExternalSku(trimmed));
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToLowerInvariant();
    }
}
