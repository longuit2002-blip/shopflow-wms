using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Domain.ProductMappings;

/// <summary>
/// Per-tenant SKU bridge row binding one marketplace
/// <see cref="ExternalSku"/> to one internal SKU per Sprint-4 plan U1/U6.
/// Three-tier production:
/// <list type="bullet">
///   <item><description><see cref="MappingMethod.Exact"/> — admin POST seeds the row; confidence 1.0.</description></item>
///   <item><description><see cref="MappingMethod.Fuzzy"/> — sync engine resolves a candidate above threshold; confidence in (<see cref="MinFuzzyConfidence"/>, 1.0).</description></item>
///   <item><description><see cref="MappingMethod.Manual"/> — operator override forces confidence 1.0 because the human is authoritative.</description></item>
/// </list>
/// The UNIQUE constraint on <c>(channel_id, external_sku)</c> in U2's
/// migration enforces single-mapping-per-external-sku-per-channel.
/// </summary>
public sealed class ProductMapping : BaseEntity
{
    public const decimal MinFuzzyConfidence = 0.5m;

    public Guid ChannelId { get; private set; }

    public ExternalSku ExternalSku { get; private set; } = null!;

    public string InternalSku { get; private set; } = string.Empty;

    public decimal ConfidenceScore { get; private set; }

    public MappingMethod Method { get; private set; }

    private ProductMapping() { }

    public static Result<ProductMapping> Create(
        Guid channelId,
        ExternalSku externalSku,
        string internalSku,
        MappingMethod method,
        decimal confidence
    )
    {
        ArgumentNullException.ThrowIfNull(externalSku);

        if (channelId == Guid.Empty)
        {
            return Result<ProductMapping>.Failure(
                "channel_id is required.",
                "mapping.channel_id_required"
            );
        }
        if (string.IsNullOrWhiteSpace(internalSku))
        {
            return Result<ProductMapping>.Failure(
                "internal_sku is required.",
                "mapping.internal_sku_required"
            );
        }
        if (confidence < 0m || confidence > 1m)
        {
            return Result<ProductMapping>.Failure(
                "confidence must be in [0, 1].",
                "mapping.confidence_out_of_range"
            );
        }

        // Method-specific invariants per Sprint-4 plan U1 test scenarios.
        var effectiveConfidence = confidence;
        switch (method)
        {
            case MappingMethod.Exact:
                if (confidence != 1m)
                {
                    return Result<ProductMapping>.Failure(
                        "exact mappings must have confidence 1.0.",
                        "mapping.exact_confidence_mismatch"
                    );
                }
                break;
            case MappingMethod.Fuzzy:
                if (confidence < MinFuzzyConfidence)
                {
                    return Result<ProductMapping>.Failure(
                        $"fuzzy mappings must have confidence >= {MinFuzzyConfidence}.",
                        "mapping.fuzzy_below_threshold"
                    );
                }
                break;
            case MappingMethod.Manual:
                // Manual is authoritative: force 1.0 regardless of input.
                effectiveConfidence = 1m;
                break;
            default:
                return Result<ProductMapping>.Failure(
                    $"unknown mapping method {method}.",
                    "mapping.unknown_method"
                );
        }

        return Result<ProductMapping>.Success(
            new ProductMapping
            {
                ChannelId = channelId,
                ExternalSku = externalSku,
                InternalSku = internalSku.Trim(),
                Method = method,
                ConfidenceScore = effectiveConfidence,
            }
        );
    }
}
