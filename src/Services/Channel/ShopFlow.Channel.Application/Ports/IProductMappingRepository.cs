using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Application.Ports;

/// <summary>
/// Per-tenant repository for <see cref="ProductMapping"/> rows per Sprint-4
/// plan U6. <see cref="UpsertManualAsync"/> catches 23505 on
/// <c>(channel_id, external_sku)</c> UNIQUE for idempotent admin POSTs;
/// <see cref="FindExactAsync"/> is the lookup-by-key fast path used by
/// the resolve flow.
/// </summary>
public interface IProductMappingRepository
{
    Task<Result<ProductMapping>> UpsertManualAsync(
        Guid channelId,
        ExternalSku externalSku,
        string internalSku,
        CancellationToken ct
    );

    Task<ProductMapping?> FindExactAsync(
        Guid channelId,
        ExternalSku externalSku,
        CancellationToken ct
    );

    Task<IReadOnlyList<ProductMapping>> ListByChannelAsync(
        Guid channelId,
        int page,
        int pageSize,
        CancellationToken ct
    );

    /// <summary>
    /// Read all mappings for one channel — used by the in-process fuzzy
    /// matcher. Sprint-5+ may swap this for a streaming-aware cursor when
    /// catalogue size makes the full read expensive.
    /// </summary>
    Task<IReadOnlyList<ProductMapping>> ReadAllByChannelAsync(Guid channelId, CancellationToken ct);
}
