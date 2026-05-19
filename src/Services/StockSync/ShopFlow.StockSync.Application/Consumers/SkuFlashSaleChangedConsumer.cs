using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Inventory;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.Application.Consumers;

/// <summary>
/// Subscribes to <see cref="SkuFlashSaleChangedV1"/> emitted by Inventory's
/// <c>SkuRepository.UpdateFlashSaleAsync</c> on every flash-sale state flip
/// (Sprint-7.5 U5 — closes Sprint-6 trade-off #10).
/// </summary>
/// <remarks>
/// <para>Calls <see cref="ISkuFlagRepository.ApplyEventAsync"/> which
/// combines the existing UNIQUE-23505 idempotent upsert (Sprint-5 U7
/// KTD7) with an OccurredAt-vs-stored guard (Sprint-7.5 KTD3) — when
/// the event's <c>OccurredAt</c> is older than the stored row's
/// <c>UpdatedAt ?? CreatedAt</c>, the write is rejected as stale. This
/// keeps flap A→B→A converging on the final intended state even under
/// W6 competing consumers that reorder per-(tenant, sku) deliveries.</para>
///
/// <para>Identical-event redelivery (same OccurredAt, same value) lands
/// in the same UpdatedAt-vs-OccurredAt branch (storedAt == occurredAt
/// is NOT &gt; so the no-op flag flips through; the aggregate's
/// idempotent SetFlashSale ensures no UpdatedAt churn either). The
/// consumer needs no message-id dedup table.</para>
/// </remarks>
public sealed class SkuFlashSaleChangedConsumer : IConsumer<SkuFlashSaleChangedV1>
{
    private readonly ISkuFlagRepository _skuFlagRepo;
    private readonly ILogger<SkuFlashSaleChangedConsumer> _logger;

    public SkuFlashSaleChangedConsumer(
        ISkuFlagRepository skuFlagRepo,
        ILogger<SkuFlashSaleChangedConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(skuFlagRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _skuFlagRepo = skuFlagRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SkuFlashSaleChangedV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;

        var applied = await _skuFlagRepo
            .ApplyEventAsync(
                tenantId: msg.TenantId,
                sku: msg.Sku,
                isFlashSale: msg.IsFlashSale,
                occurredAt: msg.OccurredAt,
                ct: context.CancellationToken)
            .ConfigureAwait(false);

        if (!applied)
        {
            _logger.LogDebug(
                "SkuFlashSaleChangedConsumer: stale event dropped (sku={Sku}, " +
                "occurredAt={OccurredAt}); stored UpdatedAt newer.",
                msg.Sku,
                msg.OccurredAt);
        }
    }
}
