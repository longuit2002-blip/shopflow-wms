using System.Diagnostics;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="ReserveStockV1"/> per Sprint-3-redux
/// K1/K11. Translates the N-line saga command into ONE atomic call against
/// <see cref="IReservationRepository.TryReserveLinesAsync"/> — N rows
/// inserted in one CTE under READ COMMITTED; if any line oversells the
/// whole call is a no-op. Emits either <see cref="StockReservedV1"/>
/// (all reserved) or <see cref="StockReservationFailedV1"/> (atomic
/// failure) through the inventory outbox; the dispatcher carries it to
/// the saga.
/// </summary>
/// <remarks>
/// <para>Pattern: mirrors Sprint-2-redux's <see cref="InboundConfirmedConsumer"/>
/// for the RequestContext binding + defense-in-depth tenant assertion. The
/// envelope <c>tenant_id</c> header is bound by the consumer middleware
/// (Sprint-2-redux U7's <c>AddShopFlowDefaults</c>); we re-validate the
/// payload's <see cref="ReserveStockV1.TenantId"/> against the bound
/// tenant so a misrouted message fails loud (DLQ) instead of writing to
/// the wrong tenant DB.</para>
///
/// <para>Idempotency: the repository's composite UNIQUE
/// <c>(order_id, order_line_id)</c> catches redelivery via 23505 →
/// re-reads + returns existing rows + the consumer publishes
/// <see cref="StockReservedV1"/> again. The saga's
/// <see cref="MassTransit.CorrelateById"/> dedups duplicate state
/// transitions.</para>
/// </remarks>
public sealed class ReserveStockConsumer : IConsumer<ReserveStockV1>
{
    private readonly IReservationRepository _repo;
    private readonly InventoryDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReserveStockConsumer> _logger;

    public ReserveStockConsumer(
        IReservationRepository repo,
        InventoryDbContext db,
        IRequestContext requestContext,
        TimeProvider clock,
        ILogger<ReserveStockConsumer> logger
    )
    {
        _repo = repo;
        _db = db;
        _requestContext = requestContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReserveStockV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var ct = context.CancellationToken;

        EnsureTenantBinding(msg.TenantId);

        // Convert the wire-format lines to domain LineReservation. Sku.Create
        // validates length; Quantity.From validates non-negative. Either
        // throws on bad input → DLQ.
        var lines = msg
            .Lines.Select(l => new LineReservation(
                Sku.Create(l.Sku),
                l.OrderLineId,
                Quantity.From(l.Qty)
            ))
            .ToArray();

        var orderId = msg.OrderId.ToString();
        var result = await _repo
            .TryReserveLinesAsync(orderId, lines, msg.Ttl, ct)
            .ConfigureAwait(false);

        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        if (result.IsSuccess)
        {
            var outcomes = result
                .LineOutcomes.Select(o => new LineOutcomeV1(
                    o.OrderLineId,
                    o.Sku.Value,
                    o.ReservationId,
                    o.Status.ToString()
                ))
                .ToArray();
            EnqueueOutbox(
                new StockReservedV1(msg.OrderId, msg.TenantId, outcomes, occurredAt),
                occurredAt
            );

            _logger.LogInformation(
                "ReserveStockV1 applied for order={OrderId} lines={LineCount}.",
                msg.OrderId,
                lines.Length
            );
        }
        else
        {
            var outcomes = result
                .LineOutcomes.Select(o => new LineOutcomeV1(
                    o.OrderLineId,
                    o.Sku.Value,
                    o.ReservationId,
                    o.Status.ToString()
                ))
                .ToArray();
            EnqueueOutbox(
                new StockReservationFailedV1(msg.OrderId, msg.TenantId, outcomes, occurredAt),
                occurredAt
            );

            _logger.LogInformation(
                "ReserveStockV1 atomic failure for order={OrderId}: {ErrorCode}.",
                msg.OrderId,
                result.ErrorCode
            );
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void EnsureTenantBinding(Guid payloadTenantId)
    {
        var bound = _requestContext.TenantId;
        if (bound != payloadTenantId)
        {
            throw new InvalidOperationException(
                $"ReserveStockV1 payload TenantId {payloadTenantId} does not match envelope TenantId {bound}. Routing fault — message rejected."
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
