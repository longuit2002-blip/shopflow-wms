using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Channel;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.Application.Consumers;

/// <summary>
/// Channel → Outbound bridge per Sprint-4 plan R7/U8. Receives
/// <see cref="OrderImportedV1"/> via MassTransit (K13 Send-routed), creates
/// the corresponding <see cref="Order"/> aggregate idempotently, and
/// enqueues the canonical <see cref="OrderPlacedV1"/> outbox row so the
/// Sprint-3 fulfillment saga starts from its existing entry point. No
/// self-HTTP loopback — re-uses the Application-layer ports the
/// OrdersController already drives.
/// </summary>
public sealed class OrderImportedConsumer : IConsumer<OrderImportedV1>
{
    private readonly IOrderRepository _orderRepo;
    private readonly IUnitOfWork _uow;
    private readonly IOutboundOutbox _outbox;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderImportedConsumer> _logger;

    private static readonly string OrderPlacedV1EventType =
        typeof(OrderPlacedV1).AssemblyQualifiedName!;

    public OrderImportedConsumer(
        IOrderRepository orderRepo,
        IUnitOfWork uow,
        IOutboundOutbox outbox,
        IRequestContext requestContext,
        TimeProvider clock,
        ILogger<OrderImportedConsumer> logger
    )
    {
        _orderRepo = orderRepo;
        _uow = uow;
        _outbox = outbox;
        _requestContext = requestContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderImportedV1> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        if (msg.Lines is null || msg.Lines.Count == 0)
        {
            _logger.LogWarning(
                "OrderImportedV1 for ChannelExternalOrderId={ExternalOrderId} has no lines; skipping.",
                msg.ChannelExternalOrderId
            );
            return;
        }

        // Idempotency short-circuit on duplicate (channel_external_order_id):
        // the saga has already been launched; ack + return.
        var existing = await _orderRepo
            .FindByExternalIdAsync(msg.ChannelExternalOrderId, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Duplicate OrderImportedV1 for ChannelExternalOrderId={ExternalOrderId}; existing Order {OrderId}.",
                msg.ChannelExternalOrderId,
                existing.Id
            );
            return;
        }

        var orderResult = Order.Create(
            msg.ChannelExternalOrderId,
            msg.ShippingProfile,
            msg.Lines.Select(l => ((string)l.Sku, (int)l.Qty, (int?)null))
        );
        if (!orderResult.IsSuccess)
        {
            _logger.LogError(
                "OrderImportedV1 -> Order.Create failed: {ErrorCode} {Error}",
                orderResult.ErrorCode,
                orderResult.Error
            );
            return;
        }

        var order = orderResult.Value!;
        await _orderRepo.AddAsync(order, ct).ConfigureAwait(false);

        var placedAt = _clock.GetUtcNow().UtcDateTime;
        var placedPayload = new OrderPlacedV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId,
            ChannelExternalOrderId: order.ChannelExternalOrderId,
            ShippingProfile: order.ShippingProfile,
            Lines: order
                .Lines.Select(l => new OrderPlacedLineV1(
                    OrderLineId: l.Id.ToString(),
                    Sku: l.Sku,
                    Qty: l.Qty,
                    ExpectedWeight: l.ExpectedWeight
                ))
                .ToArray(),
            OccurredAt: placedAt
        );
        await _outbox
            .AppendAsync(OrderPlacedV1EventType, placedPayload, ct)
            .ConfigureAwait(false);

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Channel -> Outbound: imported Order {OrderId} from {ChannelId} (external={ExternalOrderId}).",
            order.Id,
            msg.ChannelId,
            order.ChannelExternalOrderId
        );
    }
}
