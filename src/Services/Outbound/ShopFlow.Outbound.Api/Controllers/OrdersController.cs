using Microsoft.AspNetCore.Mvc;
using ShopFlow.Outbound.Api.Contracts;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Api.Controllers;

/// <summary>
/// Operator-facing HTTP surface for the order fulfillment flow per
/// Sprint-3-redux plan R11. Controllers stay thin: validate input,
/// drive the Domain aggregate, map <see cref="Result"/> to HTTP status
/// via <c>ProblemDetails</c> on failure. Mirrors Sprint-2-redux's
/// <c>PurchaseOrdersController</c>.
/// </summary>
/// <remarks>
/// <para>U2 wires the manual <c>POST /api/outbound/orders</c> (with
/// idempotent <c>channel_external_order_id</c>) + <c>GET /api/outbound/orders/{id}</c>.
/// The four saga-driving endpoints (<c>POST /{id}/confirm-pick</c>,
/// <c>POST /{id}/mark-pick-failed</c>, <c>POST /{id}/confirm-pack</c>,
/// <c>POST /{id}/confirm-ship</c>) are 501 stubs until U6/U7 land —
/// they require the saga (U4) + the pick/pack/ship orchestration
/// (U6/U7) to be in place.</para>
///
/// <para>The Create flow stamps the order as
/// <see cref="OrderStatus.Created"/> and enqueues a stub
/// <c>OrderPlacedV1</c> payload to <c>outbound_outbox_messages</c>
/// (atomic with the order insert). The dispatcher (U1) drains the
/// outbox; the saga (U4) consumes <c>OrderPlacedV1</c> on the bus and
/// drives the order forward. Until U3 ships the canonical contract
/// type <c>ShopFlow.Contracts.Outbound.OrderPlacedV1</c>, U2 writes the
/// payload using a local stub type — see the TODO inside
/// <see cref="CreateAsync"/>.</para>
/// </remarks>
[ApiController]
[Route("api/outbound/orders")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>
    /// Provisional wire-format event type for <c>OrderPlacedV1</c>. U3
    /// replaces this with the canonical
    /// <c>typeof(ShopFlow.Contracts.Outbound.OrderPlacedV1).AssemblyQualifiedName</c>.
    /// </summary>
    // TODO(U3): swap for ShopFlow.Contracts.Outbound.OrderPlacedV1's
    // AssemblyQualifiedName once the contract type lands.
    internal const string OrderPlacedV1EventType =
        "ShopFlow.Contracts.Outbound.OrderPlacedV1, ShopFlow.Contracts";

    private readonly IOrderRepository _orderRepo;
    private readonly IUnitOfWork _uow;
    private readonly IOutboundOutbox _outbox;
    private readonly TimeProvider _clock;

    public OrdersController(
        IOrderRepository orderRepo,
        IUnitOfWork uow,
        IOutboundOutbox outbox,
        TimeProvider clock
    )
    {
        _orderRepo = orderRepo;
        _uow = uow;
        _outbox = outbox;
        _clock = clock;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return ProblemFromError("request body is required.", "order.request_required", 400);
        }

        // Idempotency short-circuit: same channel_external_order_id twice
        // returns the existing order. The UNIQUE index on the column
        // (plan R1) is defence in depth against a concurrent race.
        if (!string.IsNullOrWhiteSpace(request.ChannelExternalOrderId))
        {
            var existing = await _orderRepo
                .FindByExternalIdAsync(request.ChannelExternalOrderId.Trim(), ct)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return Ok(Map(existing));
            }
        }

        var orderResult = Order.Create(
            request.ChannelExternalOrderId,
            request.ShippingProfile,
            request.Lines?.Select(l => (l.Sku, l.Qty, l.ExpectedWeight))
                ?? Array.Empty<(string, int, int?)>()
        );
        if (!orderResult.IsSuccess)
        {
            return ProblemFromResult(orderResult.Error!, orderResult.ErrorCode!);
        }

        var order = orderResult.Value!;
        await _orderRepo.AddAsync(order, ct).ConfigureAwait(false);

        // TODO(U3): swap the anonymous stub payload for the canonical
        // ShopFlow.Contracts.Outbound.OrderPlacedV1 record once the
        // contract type ships. The wire-format JSON is stable across
        // the swap because OutboxJsonOptions.Default uses camelCase
        // property naming and the field set is the same.
        var placedAt = _clock.GetUtcNow().UtcDateTime;
        var placedPayload = new
        {
            OrderId = order.Id,
            ChannelExternalOrderId = order.ChannelExternalOrderId,
            ShippingProfile = order.ShippingProfile,
            Lines = order
                .Lines.Select(l => new
                {
                    OrderLineId = l.Id,
                    Sku = l.Sku,
                    Qty = l.Qty,
                    ExpectedWeight = l.ExpectedWeight,
                })
                .ToArray(),
            OccurredAt = placedAt,
        };
        await _outbox
            .AppendAsync(OrderPlacedV1EventType, placedPayload, ct)
            .ConfigureAwait(false);

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetByIdAsync), new { id = order.Id }, Map(order));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }
        return Ok(Map(order));
    }

    [HttpPost("{id:guid}/confirm-pick")]
    public IActionResult ConfirmPick(Guid id)
    {
        _ = id;
        return Problem(
            statusCode: 501,
            title: "POST /api/outbound/orders/{id}/confirm-pick ships in Sprint-3-redux U6.",
            type: "https://shopflow.example/errors/not_implemented"
        );
    }

    [HttpPost("{id:guid}/mark-pick-failed")]
    public IActionResult MarkPickFailed(Guid id)
    {
        _ = id;
        return Problem(
            statusCode: 501,
            title:
                "POST /api/outbound/orders/{id}/mark-pick-failed ships in Sprint-3-redux U7.",
            type: "https://shopflow.example/errors/not_implemented"
        );
    }

    [HttpPost("{id:guid}/confirm-pack")]
    public IActionResult ConfirmPack(Guid id)
    {
        _ = id;
        return Problem(
            statusCode: 501,
            title: "POST /api/outbound/orders/{id}/confirm-pack ships in Sprint-3-redux U6.",
            type: "https://shopflow.example/errors/not_implemented"
        );
    }

    [HttpPost("{id:guid}/confirm-ship")]
    public IActionResult ConfirmShip(Guid id)
    {
        _ = id;
        return Problem(
            statusCode: 501,
            title: "POST /api/outbound/orders/{id}/confirm-ship ships in Sprint-3-redux U6.",
            type: "https://shopflow.example/errors/not_implemented"
        );
    }

    private IActionResult ProblemFromError(string detail, string code, int status) =>
        Problem(
            statusCode: status,
            title: detail,
            type: $"https://shopflow.example/errors/{code}"
        );

    private IActionResult ProblemFromResult(string detail, string code)
    {
        var status = code.EndsWith("not_found", StringComparison.Ordinal) ? 404 : 400;
        return ProblemFromError(detail, code, status);
    }

    private static OrderResponse Map(Order order) =>
        new(
            Id: order.Id,
            ChannelExternalOrderId: order.ChannelExternalOrderId,
            ShippingProfile: order.ShippingProfile,
            Status: order.Status.ToString(),
            ExpectedWeightTotal: order.ExpectedWeightTotal,
            ActualWeightTotal: order.ActualWeightTotal,
            LabelUrl: order.LabelUrl,
            TrackingNumber: order.TrackingNumber,
            PickWaveId: order.PickWaveId,
            Lines: order
                .Lines.Select(l => new OrderLineResponse(l.Id, l.Sku, l.Qty, l.ExpectedWeight))
                .ToArray()
        );
}
