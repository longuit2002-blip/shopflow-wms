using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Inbound;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="InboundConfirmedV1"/> per
/// Sprint-2-redux plan R10-R15. Applies the per-line stock change to
/// the receiving tenant DB:
/// </summary>
/// <list type="number">
///   <item><description>Read <c>tenant_id</c> from the message header and bind the ambient <see cref="RequestContext"/> (the per-request DbContext factory + repositories then resolve to the correct tenant DB).</description></item>
///   <item><description>Try to record an <c>inbound_dedup</c> row via <see cref="IInboundDedupRepository.TryRecordAsync"/>; on duplicate, ACK without further writes (Sprint-2-redux R11).</description></item>
///   <item><description>Apply the stock change via the bin-aware <see cref="IStockItemRepository.AdjustAtBinAsync"/> — auto-creates <c>stock_items</c> for unknown SKU, increments <c>stock_item_bins.quantity</c>, increments <c>stock_items.available</c>, increments <c>bins.occupancy_qty</c>, writes <c>stock_adjustments</c> audit row.</description></item>
/// </list>
/// <remarks>
/// Both steps share the same tenant DB; the dedup write and the stock
/// adjustment commit independently (dedup first to gate the adjustment).
/// On adjustment failure after dedup commit, the redelivery loop re-runs
/// step 1 → ACK without re-applying. This is acceptable per Sprint-2-redux
/// because the only failure mode of <see cref="IStockItemRepository.AdjustAtBinAsync"/>
/// is <c>stock.bin_underflow</c> on negative delta — Inbound never sends
/// negative deltas, so the path is robust. If a future Inbound flow sends
/// negative deltas, this consumer should be reshaped to wrap both steps
/// in a single transaction.
/// </remarks>
public sealed class InboundConfirmedConsumer : IConsumer<InboundConfirmedV1>
{
    private readonly IInboundDedupRepository _dedup;
    private readonly IStockItemRepository _stockRepo;
    private readonly RequestContext _requestContext;
    private readonly ILogger<InboundConfirmedConsumer> _logger;

    public InboundConfirmedConsumer(
        IInboundDedupRepository dedup,
        IStockItemRepository stockRepo,
        RequestContext requestContext,
        ILogger<InboundConfirmedConsumer> logger
    )
    {
        _dedup = dedup;
        _stockRepo = stockRepo;
        _requestContext = requestContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<InboundConfirmedV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var ct = context.CancellationToken;

        // The tenant has already been bound by the consumer middleware
        // (registered by AddShopFlowDefaults in U7) from the tenant_id
        // header on the envelope. Trust the binding — re-validating here
        // would be ShopFlow0004.
        var tenantId = _requestContext.TenantId;
        if (tenantId != msg.TenantId)
        {
            // Defense in depth: if the envelope header points at a
            // different tenant than the payload carries, the routing
            // layer is broken — drop the message into the DLQ.
            throw new InvalidOperationException(
                $"InboundConfirmedV1 payload TenantId {msg.TenantId} does not match envelope TenantId {tenantId}. Routing fault — message rejected."
            );
        }

        var processedAt = DateTime.UtcNow;
        var fresh = await _dedup
            .TryRecordAsync(
                receivingId: msg.ReceivingId,
                lineId: msg.PurchaseOrderLineId,
                sku: msg.Sku,
                quantity: msg.ActualQuantity,
                processedAt: processedAt,
                ct: ct
            )
            .ConfigureAwait(false);

        if (!fresh)
        {
            _logger.LogInformation(
                "InboundConfirmedV1 duplicate delivery for (receiving={ReceivingId}, line={LineId}); ACK without re-applying.",
                msg.ReceivingId,
                msg.PurchaseOrderLineId
            );
            return;
        }

        var adjustResult = await _stockRepo
            .AdjustAtBinAsync(
                sku: Sku.Create(msg.Sku),
                binId: msg.BinId,
                delta: msg.ActualQuantity,
                reason: StockAdjustmentReason.Receipt,
                note: $"PO line {msg.PurchaseOrderLineId} via receiving {msg.ReceivingId}",
                ct: ct
            )
            .ConfigureAwait(false);

        if (!adjustResult.IsSuccess)
        {
            // Non-transient failure (e.g., bin underflow). Log and surface
            // for DLQ; the dedup row stays so a retry doesn't re-apply.
            _logger.LogError(
                "InboundConfirmedV1 adjustment failed for (receiving={ReceivingId}, line={LineId}, sku={Sku}): {ErrorCode} {Error}",
                msg.ReceivingId,
                msg.PurchaseOrderLineId,
                msg.Sku,
                adjustResult.ErrorCode,
                adjustResult.Error
            );
            throw new InvalidOperationException(
                $"Stock adjustment rejected: {adjustResult.ErrorCode} — {adjustResult.Error}"
            );
        }

        _logger.LogInformation(
            "InboundConfirmedV1 applied for (receiving={ReceivingId}, line={LineId}, sku={Sku}, qty={Qty}, bin={Bin}).",
            msg.ReceivingId,
            msg.PurchaseOrderLineId,
            msg.Sku,
            msg.ActualQuantity,
            msg.BinId
        );
    }
}
