using ShopFlow.Channel.Domain.ProductMappings;

namespace ShopFlow.Channel.Application.Ports;

/// <summary>
/// Three-tier resolver for <c>(channel_id, external_sku) → internal_sku</c>
/// per Sprint-4 plan R6/U6. Exact match first (1.0 confidence), fuzzy
/// fallback above threshold, null on miss. Manual mappings are written
/// directly via the repository's <see cref="IProductMappingRepository.UpsertManualAsync"/>;
/// the service is read-side only.
/// </summary>
public interface IProductMappingService
{
    Task<ProductMappingResolution?> ResolveAsync(
        Guid channelId,
        string externalSku,
        CancellationToken ct
    );
}

/// <summary>
/// One resolved mapping. <see cref="MappingMethod.Manual"/> + Exact carry
/// confidence 1.0; Fuzzy carries the match score.
/// </summary>
public sealed record ProductMappingResolution(
    string InternalSku,
    MappingMethod Method,
    decimal Confidence
);
