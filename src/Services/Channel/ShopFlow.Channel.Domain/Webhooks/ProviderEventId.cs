using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Domain.Webhooks;

/// <summary>
/// Marketplace-supplied event id — the second half of the
/// <c>(channel_id, provider_event_id)</c> UNIQUE key that makes webhook
/// processing idempotent per Tech Design v3.0 §6. Trimmed, non-empty,
/// bounded at 200 chars at the domain (Postgres TEXT has no hard limit but
/// Shopee/Lazada event ids are O(40 chars); 200 leaves headroom for legacy
/// providers without inviting payload abuse).
/// </summary>
/// <remarks>
/// Equality is case-sensitive ordinal — marketplaces emit event ids that
/// are typically alphanumeric and case-significant (Shopee uses lower-case
/// hex; Lazada uses base62). Don't fold case here.
/// </remarks>
public sealed class ProviderEventId : ValueObject
{
    public const int MaxLength = 200;

    public string Value { get; }

    private ProviderEventId(string value)
    {
        Value = value;
    }

    public static Result<ProviderEventId> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ProviderEventId>.Failure(
                "provider_event_id is required.",
                "webhook.provider_event_id_required"
            );
        }
        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            return Result<ProviderEventId>.Failure(
                $"provider_event_id must be {MaxLength} characters or fewer; got {trimmed.Length}.",
                "webhook.provider_event_id_too_long"
            );
        }
        return Result<ProviderEventId>.Success(new ProviderEventId(trimmed));
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
