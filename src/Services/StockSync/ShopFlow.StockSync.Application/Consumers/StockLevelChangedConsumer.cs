using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Inventory;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.Application.Consumers;

/// <summary>
/// Subscribes to <see cref="StockLevelChangedV1"/> emitted by Inventory's
/// reservation + put-away repositories (Sprint-5 plan U2). For each
/// message, resolves the tenant's active channel slugs and the SKU's
/// flash-sale flag, then writes one <see cref="CoalesceEntry"/> per
/// channel into the in-memory <see cref="ICoalescingBuffer"/> (Sprint-5
/// plan U3 / R5 mirror-all). The flush <c>BackgroundService</c> drains
/// the buffer downstream.
/// </summary>
/// <remarks>
/// <para>Idempotency: <see cref="ICoalescingBuffer.Upsert"/> is a
/// last-by-<c>ObservedAt</c> tiebreaker, so a MassTransit redelivery
/// (same payload, same <c>OccurredAt</c>) is a no-op. The consumer needs
/// no message-id dedup table.</para>
///
/// <para>The consumer does NOT bind <c>RequestContext</c> or open a
/// scoped DbContext — the buffer + ports are singletons and the flush
/// path is the one that opens per-tenant scopes when persisting push-log
/// rows (Sprint-5 U5). This keeps the consume path off the hot DB-write
/// path during flash sales.</para>
/// </remarks>
public sealed class StockLevelChangedConsumer : IConsumer<StockLevelChangedV1>
{
    private readonly ICoalescingBuffer _buffer;
    private readonly IChannelLookupPort _channelLookup;
    private readonly ISkuFlagRepository _skuFlagRepo;
    private readonly ILogger<StockLevelChangedConsumer> _logger;

    public StockLevelChangedConsumer(
        ICoalescingBuffer buffer,
        IChannelLookupPort channelLookup,
        ISkuFlagRepository skuFlagRepo,
        ILogger<StockLevelChangedConsumer> logger
    )
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(channelLookup);
        ArgumentNullException.ThrowIfNull(skuFlagRepo);
        ArgumentNullException.ThrowIfNull(logger);

        _buffer = buffer;
        _channelLookup = channelLookup;
        _skuFlagRepo = skuFlagRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockLevelChangedV1> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        var channels = await _channelLookup
            .GetActiveChannelsAsync(msg.TenantId, ct)
            .ConfigureAwait(false);

        if (channels.Count == 0)
        {
            _logger.LogDebug(
                "StockLevelChangedV1 for tenant {TenantId} sku {Sku} has no active channels; skipping coalesce write.",
                msg.TenantId,
                msg.Sku
            );
            return;
        }

        var isFlashSale = await _skuFlagRepo
            .IsFlashSaleAsync(msg.Sku, ct)
            .ConfigureAwait(false);

        foreach (var channelType in channels)
        {
            var key = new CoalesceKey(msg.TenantId, msg.Sku, channelType);
            var entry = new CoalesceEntry(
                AvailableToSell: msg.AvailableToSell,
                ObservedAt: msg.OccurredAt,
                IsFlashSale: isFlashSale
            );
            _buffer.Upsert(key, entry);
        }

        _logger.LogDebug(
            "Coalesced StockLevelChangedV1 tenant={TenantId} sku={Sku} available={Available} channels={ChannelCount} flash={Flash}",
            msg.TenantId,
            msg.Sku,
            msg.AvailableToSell,
            channels.Count,
            isFlashSale
        );
    }
}
