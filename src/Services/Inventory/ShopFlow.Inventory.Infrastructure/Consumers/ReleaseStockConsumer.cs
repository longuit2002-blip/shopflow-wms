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
/// MassTransit consumer for <see cref="ReleaseStockV1"/> per Sprint-3-redux
/// U3/U7. Branches on <see cref="ReleaseStockV1.OrderLineIds"/>:
/// empty list ⇒ <see cref="IReservationRepository.ReleaseAsync"/> (full
/// release of all Pending rows for the order); non-empty ⇒
/// <see cref="IReservationRepository.ReleaseLinesAsync"/> (partial set
/// release for saga compensation). Emits
/// <see cref="StockReleasedV1"/> with the actually-released line ids
/// (the saga's <c>ReleasedLineSkus</c> dedup relies on this set).
/// </summary>
/// <remarks>
/// Idempotency: ALREADY_RELEASED on full-release redelivery is treated as
/// success (emits StockReleasedV1 with empty list — the saga's dedup
/// drops it). Partial-set redelivery: rows already in Released state skip
/// silently in the SQL guard <c>status='Pending'</c>; the returned line
/// id list reflects only the rows that actually transitioned in this
/// call.
/// </remarks>
public sealed class ReleaseStockConsumer : IConsumer<ReleaseStockV1>
{
    private readonly IReservationRepository _repo;
    private readonly InventoryDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReleaseStockConsumer> _logger;

    public ReleaseStockConsumer(
        IReservationRepository repo,
        InventoryDbContext db,
        IRequestContext requestContext,
        TimeProvider clock,
        ILogger<ReleaseStockConsumer> logger
    )
    {
        _repo = repo;
        _db = db;
        _requestContext = requestContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReleaseStockV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var ct = context.CancellationToken;

        EnsureTenantBinding(msg.TenantId);

        var orderId = msg.OrderId.ToString();
        IReadOnlyList<string> releasedLineIds;
        if (msg.OrderLineIds is null || msg.OrderLineIds.Count == 0)
        {
            // Full-order release.
            var result = await _repo.ReleaseAsync(orderId, ct).ConfigureAwait(false);
            if (!result.IsSuccess && result.ErrorCode != "reservation.already_released")
            {
                _logger.LogWarning(
                    "ReleaseStockV1 (full) for order={OrderId} returned {ErrorCode}: {Error} — emitting StockReleasedV1 with empty list (idempotent).",
                    msg.OrderId,
                    result.ErrorCode,
                    result.Error
                );
            }
            // For full release we don't have the per-line list back from
            // ReleaseAsync; surface an empty list (the saga's compensation
            // dedup treats empty as "all already released").
            releasedLineIds = Array.Empty<string>();
        }
        else
        {
            // Partial-set release.
            var result = await _repo
                .ReleaseLinesAsync(orderId, msg.OrderLineIds, ct)
                .ConfigureAwait(false);
            releasedLineIds = result.ReleasedLineIds;
        }

        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        EnqueueOutbox(
            new StockReleasedV1(msg.OrderId, msg.TenantId, releasedLineIds, occurredAt),
            occurredAt
        );
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "ReleaseStockV1 applied for order={OrderId} releasedLineCount={Count}.",
            msg.OrderId,
            releasedLineIds.Count
        );
    }

    private void EnsureTenantBinding(Guid payloadTenantId)
    {
        var bound = _requestContext.TenantId;
        if (bound != payloadTenantId)
        {
            throw new InvalidOperationException(
                $"ReleaseStockV1 payload TenantId {payloadTenantId} does not match envelope TenantId {bound}. Routing fault — message rejected."
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
