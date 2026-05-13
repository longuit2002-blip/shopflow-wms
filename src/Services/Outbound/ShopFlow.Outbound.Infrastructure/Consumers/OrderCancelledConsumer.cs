using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Consumers;

/// <summary>
/// Sprint-3-redux U7 — closes the saga's compensation loop by writing
/// the Order aggregate's terminal Cancelled state. Triggered by the
/// in-process <see cref="OrderCancelled"/> event the saga publishes on
/// entering its <c>Cancelled</c> state.
/// </summary>
/// <remarks>
/// <para>Per R3 eventual-consistency: the saga's transition-to-Cancelled
/// commit and this consumer's Order row update live in separate EF
/// transactions. The window between the two is typically sub-second; an
/// operator <c>GET /orders/{id}</c> may briefly observe the Order at
/// <c>CompensatingReservation</c> after the saga has reached its own
/// <c>Cancelled</c> state and before this consumer commits the flip.</para>
///
/// <para>Idempotency: <see cref="Order.MarkCancelled"/> returns the
/// <c>order.already_cancelled</c> code on a re-delivery (the message is
/// retried by MT) — the consumer logs at info and ACKs, treating the
/// redelivery as success. <c>order.invalid_state</c> on the
/// already-Shipped path is genuinely bad (saga shouldn't have transitioned
/// to Cancelled for a Shipped order); surface a warning so the failure
/// shows in logs while still ACKing (no retry recovery — the Order row
/// is in a contradictory state that needs manual investigation).</para>
///
/// <para>Auto-registered via <c>AddConsumers(asm)</c> in the kernel-wide
/// <c>AddShopFlowDefaults</c> MassTransit configuration — the Outbound
/// Infrastructure assembly is one of the scanned assemblies.</para>
/// </remarks>
public sealed class OrderCancelledConsumer : IConsumer<OrderCancelled>
{
    private readonly IOrderRepository _orderRepo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<OrderCancelledConsumer> _logger;

    public OrderCancelledConsumer(
        IOrderRepository orderRepo,
        IUnitOfWork uow,
        ILogger<OrderCancelledConsumer> logger
    )
    {
        _orderRepo = orderRepo;
        _uow = uow;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.CancellationToken;
        var orderId = context.Message.OrderId;

        var order = await _orderRepo.FindByIdAsync(orderId, ct).ConfigureAwait(false);
        if (order is null)
        {
            // Saga emitted OrderCancelled for an order the local DB doesn't
            // know about. Could happen on a per-tenant mis-routing — the
            // saga's tenant DB doesn't match the consumer's tenant DB. Log
            // + ACK; the saga's row still records the cancellation as the
            // operator-facing audit trail.
            _logger.LogWarning(
                "OrderCancelled consumer: order {OrderId} not found in tenant DB; saga is at Cancelled but Order row absent.",
                orderId
            );
            return;
        }

        var result = order.MarkCancelled();
        if (!result.IsSuccess)
        {
            if (result.ErrorCode == "order.already_cancelled")
            {
                // Redelivery — Order row already at Cancelled. ACK as success.
                _logger.LogInformation(
                    "OrderCancelled consumer: order {OrderId} already at Cancelled (redelivery — no-op).",
                    orderId
                );
                return;
            }
            // Genuinely unexpected (e.g., Shipped order); surface but still ACK.
            _logger.LogWarning(
                "OrderCancelled consumer: order {OrderId} in {Status} state — cannot transition to Cancelled ({Code}: {Error}). Manual investigation required.",
                orderId,
                order.Status,
                result.ErrorCode,
                result.Error
            );
            return;
        }

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "OrderCancelled consumer: order {OrderId} transitioned to Cancelled.",
            orderId
        );
    }
}
