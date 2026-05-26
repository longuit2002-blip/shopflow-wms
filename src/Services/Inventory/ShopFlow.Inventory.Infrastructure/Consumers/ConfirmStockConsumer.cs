using System.Diagnostics;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="ConfirmStockV1"/> per Sprint-3-redux
/// U3. Drives the whole-order Pending → Confirmed transition through
/// <see cref="IReservationRepository.ConfirmAsync"/>; for multi-line orders
/// this transitions N rows in one SQL statement (the existing WHERE
/// <c>order_id = X</c> already matches them all). Emits
/// <see cref="StockConfirmedV1"/> through the inventory outbox.
/// </summary>
/// <remarks>
/// Idempotency: ALREADY_CONFIRMED on redelivery is treated as success —
/// the consumer emits <see cref="StockConfirmedV1"/> again so the saga's
/// side-effect notification stays consistent. NOT_FOUND is also success
/// (the order may have been released or never reserved; either way the
/// saga has already moved on).
/// </remarks>
public sealed class ConfirmStockConsumer : IConsumer<ConfirmStockV1>
{
    private readonly IReservationRepository _repo;
    private readonly InventoryDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<ConfirmStockConsumer> _logger;

    public ConfirmStockConsumer(
        IReservationRepository repo,
        InventoryDbContext db,
        IRequestContext requestContext,
        TimeProvider clock,
        ILogger<ConfirmStockConsumer> logger
    )
    {
        _repo = repo;
        _db = db;
        _requestContext = requestContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ConfirmStockV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var ct = context.CancellationToken;

        EnsureTenantBinding(msg.TenantId);

        var orderId = msg.OrderId.ToString();
        var result = await _repo.ConfirmAsync(orderId, ct).ConfigureAwait(false);

        // Sprint-3-redux: ALREADY_CONFIRMED is success for the outbound
        // notification — the saga relies on the side-effect event, not on
        // a return value from the consumer.
        if (!result.IsSuccess && result.ErrorCode != "reservation.already_confirmed")
        {
            _logger.LogWarning(
                "ConfirmStockV1 for order={OrderId} returned {ErrorCode}: {Error} — emitting StockConfirmedV1 anyway (idempotent).",
                msg.OrderId,
                result.ErrorCode,
                result.Error
            );
        }

        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        EnqueueOutbox(new StockConfirmedV1(msg.OrderId, msg.TenantId, occurredAt), occurredAt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("ConfirmStockV1 applied for order={OrderId}.", msg.OrderId);
    }

    private void EnsureTenantBinding(Guid payloadTenantId)
    {
        var bound = _requestContext.TenantId;
        if (bound != payloadTenantId)
        {
            throw new InvalidOperationException(
                $"ConfirmStockV1 payload TenantId {payloadTenantId} does not match envelope TenantId {bound}. Routing fault — message rejected."
            );
        }
    }

    private void EnqueueOutbox<T>(T integrationEvent, DateTime occurredAt)
        where T : class
    {
        var traceId = Activity.Current?.TraceId.ToString();
        _db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = _requestContext.TenantId,
                EventType = typeof(T).AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(
                    integrationEvent,
                    typeof(T),
                    OutboxJsonOptions.Default
                ),
                TraceId = traceId,
                CreatedAt = occurredAt,
            }
        );
    }
}
