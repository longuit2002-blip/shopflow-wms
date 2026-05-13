using MassTransit;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Api.Contracts;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;
using ShopFlow.SharedKernel.Application;
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
/// U6 ships the three saga-driving endpoints
/// (<c>POST /{id}/confirm-pick</c>, <c>POST /{id}/confirm-pack</c>,
/// <c>POST /{id}/confirm-ship</c>); U7 wires the
/// <c>POST /{id}/mark-pick-failed</c> compensation entry.</para>
///
/// <para>The Create flow stamps the order as
/// <see cref="OrderStatus.Created"/> and enqueues a stub
/// <c>OrderPlacedV1</c> payload to <c>outbound_outbox_messages</c>
/// (atomic with the order insert). The dispatcher (U1) drains the
/// outbox; the saga (U4) consumes <c>OrderPlacedV1</c> on the bus and
/// drives the order forward.</para>
///
/// <para>U6's confirm-pick / confirm-pack / confirm-ship actions follow
/// the R3 eventual-consistency boundary: the controller's
/// <see cref="IUnitOfWork.SaveChangesAsync"/> commits the order +
/// outbox rows in one EF transaction; the in-process saga event
/// (<see cref="PickConfirmed"/> / <see cref="PackConfirmed"/> /
/// <see cref="ShipConfirmed"/>) is published via
/// <see cref="IPublishEndpoint"/> and the saga's state-machine commit
/// lands in a separate MassTransit transaction.</para>
/// </remarks>
[ApiController]
[Route("api/outbound/orders")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>
    /// Weight-variance threshold above which <c>confirm-pack</c> flags a
    /// warning. Per the U6 plan spec: |actual - expected| / expected &gt; 10%.
    /// </summary>
    public const double WeightWarningThreshold = 0.10;

    /// <summary>
    /// Canonical wire-format event type for <c>OrderPlacedV1</c>. Sprint-3-redux
    /// U3 landed the contract type; this is its assembly-qualified name
    /// (the form the dispatcher's <c>Type.GetType</c> reads at dispatch
    /// time).
    /// </summary>
    internal static readonly string OrderPlacedV1EventType =
        typeof(OrderPlacedV1).AssemblyQualifiedName!;

    /// <summary>
    /// Wire-format event type for <c>ConfirmStockV1</c> — emitted on the
    /// AwaitingShip → Shipped transition so the Inventory module's
    /// <c>ConfirmStockConsumer</c> can drain Pending reservations on the
    /// confirmed order. Per K13 dispatcher uses Publish for all envelopes
    /// today; W6 mechanical split adds envelope-type → endpoint routing.
    /// </summary>
    internal static readonly string ConfirmStockV1EventType =
        typeof(ConfirmStockV1).AssemblyQualifiedName!;

    /// <summary>
    /// Wire-format event type for <c>TrackingPushedV1</c> — consumed by
    /// the stub <c>ChannelTrackingConsumer</c> in Sprint-3-redux (Phase-2
    /// Sprint-4 moves the consumer to <c>ShopFlow.Channel.Infrastructure</c>).
    /// </summary>
    internal static readonly string TrackingPushedV1EventType =
        typeof(TrackingPushedV1).AssemblyQualifiedName!;

    private readonly IOrderRepository _orderRepo;
    private readonly IUnitOfWork _uow;
    private readonly IOutboundOutbox _outbox;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IMockShippingProvider _shippingProvider;

    public OrdersController(
        IOrderRepository orderRepo,
        IUnitOfWork uow,
        IOutboundOutbox outbox,
        IRequestContext requestContext,
        TimeProvider clock,
        IPublishEndpoint publishEndpoint,
        IMockShippingProvider shippingProvider
    )
    {
        _orderRepo = orderRepo;
        _uow = uow;
        _outbox = outbox;
        _requestContext = requestContext;
        _clock = clock;
        _publishEndpoint = publishEndpoint;
        _shippingProvider = shippingProvider;
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

        // Sprint-3-redux U3: use the canonical OrderPlacedV1 contract type.
        // The wire-format JSON is unchanged from U2's anonymous stub —
        // OutboxJsonOptions.Default's camelCase naming + identical field
        // set means downstream consumers see the same bytes.
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

    /// <summary>
    /// U6 — picker reports the order is picked. Order moves
    /// AwaitingPick → Picked; saga receives <see cref="PickConfirmed"/>
    /// (in-process publish via <see cref="IPublishEndpoint"/>) and
    /// transitions to its own Picked state.
    /// </summary>
    [HttpPost("{id:guid}/confirm-pick")]
    public async Task<IActionResult> ConfirmPickAsync(Guid id, CancellationToken ct)
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        var transition = order.MarkPicked();
        if (!transition.IsSuccess)
        {
            return ProblemFromResult(transition.Error!, transition.ErrorCode!);
        }

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // Publish the in-process saga event AFTER the order commit lands —
        // MassTransit's saga middleware will pick it up on the next dispatch
        // tick and commit the saga transition in its own EF transaction.
        await _publishEndpoint.Publish(new PickConfirmed(order.Id), ct).ConfigureAwait(false);

        return Ok(Map(order));
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

    /// <summary>
    /// U6 — packer reports the actual packed weight. Weight-variance
    /// check vs. the expected weight: if &gt; 10% the response carries
    /// <c>weight_warning=true</c> with the signed variance percentage,
    /// but the transition still completes (warning is informational,
    /// not a reject). Order moves Picked → Packed → AwaitingShip in the
    /// same SaveChanges; saga receives <see cref="PackConfirmed"/> and
    /// transitions through its own Packed state on the next dispatch tick.
    /// </summary>
    [HttpPost("{id:guid}/confirm-pack")]
    public async Task<IActionResult> ConfirmPackAsync(
        Guid id,
        [FromBody] ConfirmPackRequest request,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return ProblemFromError("request body is required.", "order.request_required", 400);
        }
        if (request.ActualWeightTotal < 0)
        {
            return ProblemFromError(
                "actual_weight_total must be non-negative.",
                "order.actual_weight_negative",
                400
            );
        }

        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        // MarkPacked requires Picked pre-state per Order's state machine
        // (U2's MarkPacked_FromAwaitingPack_FailsInvalidState locks this).
        // The plan's "Picked OR AwaitingPack" wording acknowledged the U4
        // deviation but Order's state machine keeps Picked-only — see
        // deviations in U6 sign-off.
        var packTransition = order.MarkPacked(request.ActualWeightTotal);
        if (!packTransition.IsSuccess)
        {
            return ProblemFromResult(packTransition.Error!, packTransition.ErrorCode!);
        }

        // Chain Packed → AwaitingShip so the confirm-ship endpoint can
        // call MarkShipped without an explicit intermediate POST. The
        // saga model in the plan declares Packed → AwaitingShip as an
        // auto transition; the Order aggregate runs one step ahead of
        // the saga's view of state, which is fine because the saga is
        // the authoritative state column for cross-module commands and
        // the Order row is the operator-facing state.
        var awaitingShipTransition = order.MarkAwaitingShip();
        if (!awaitingShipTransition.IsSuccess)
        {
            // Should be impossible given we just transitioned to Packed;
            // surface as 500-equivalent so the operator sees the invariant
            // breach rather than the silent rollback.
            return ProblemFromResult(
                awaitingShipTransition.Error!,
                awaitingShipTransition.ErrorCode!
            );
        }

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // R3 boundary: publish the in-process saga event after the order
        // commit. Saga middleware drives the saga state forward in its
        // own EF transaction.
        await _publishEndpoint
            .Publish(new PackConfirmed(order.Id, request.ActualWeightTotal), ct)
            .ConfigureAwait(false);

        var (warning, variancePct) = ComputeWeightWarning(
            order.ExpectedWeightTotal,
            request.ActualWeightTotal
        );
        return Ok(
            new ConfirmPackResponse(
                Order: Map(order),
                WeightWarning: warning,
                WeightVariancePct: variancePct
            )
        );
    }

    /// <summary>
    /// U6 — final ship confirmation. Calls the mocked carrier (Polly
    /// pipeline handles retries); on success persists label + tracking
    /// number, enqueues <c>ConfirmStockV1</c> + <c>TrackingPushedV1</c>
    /// in the outbox (same SaveChanges), and publishes the in-process
    /// <see cref="ShipConfirmed"/> saga event. On carrier exhaustion
    /// (Polly retries exhausted) returns 503 ProblemDetails
    /// <c>shipping.carrier_unavailable</c>; the order stays in
    /// AwaitingShip; no Inventory commands published.
    /// </summary>
    [HttpPost("{id:guid}/confirm-ship")]
    public async Task<IActionResult> ConfirmShipAsync(Guid id, CancellationToken ct)
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        if (order.Status != OrderStatus.AwaitingShip)
        {
            return ProblemFromError(
                $"cannot ship order in {order.Status} state; required pre-state AwaitingShip.",
                "order.invalid_state",
                400
            );
        }

        ShippingLabel label;
        try
        {
            label = await _shippingProvider
                .CreateLabelAsync(order, ct)
                .ConfigureAwait(false);
        }
        catch (TransientShippingException ex)
        {
            // Polly retries exhausted. Order stays in AwaitingShip — no
            // state change persisted, no outbox rows enqueued. Operator
            // can retry the endpoint; the Polly pipeline will spin up
            // again. The 503 ProblemDetails carries no exception detail
            // (operator doesn't need it).
            _ = ex;
            return ProblemFromError(
                "shipping carrier unavailable after retries.",
                "shipping.carrier_unavailable",
                503
            );
        }

        var shipTransition = order.MarkShipped(label.LabelUrl, label.TrackingNumber);
        if (!shipTransition.IsSuccess)
        {
            // Defensive: should be impossible given the AwaitingShip check
            // above, but if the Domain rejects (e.g. blank label_url) we
            // surface rather than swallow.
            return ProblemFromResult(shipTransition.Error!, shipTransition.ErrorCode!);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var confirmPayload = new ConfirmStockV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId
        );
        var trackingPayload = new TrackingPushedV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId,
            TrackingNumber: label.TrackingNumber,
            LabelUrl: label.LabelUrl,
            ChannelId: null,
            OccurredAt: now
        );
        await _outbox
            .AppendAsync(ConfirmStockV1EventType, confirmPayload, ct)
            .ConfigureAwait(false);
        await _outbox
            .AppendAsync(TrackingPushedV1EventType, trackingPayload, ct)
            .ConfigureAwait(false);

        // Single SaveChanges commits the order update + both outbox rows
        // in one EF transaction. The saga commit is separate.
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        await _publishEndpoint
            .Publish(
                new ShipConfirmed(order.Id, label.LabelUrl, label.TrackingNumber),
                ct
            )
            .ConfigureAwait(false);

        return Ok(
            new ConfirmShipResponse(
                LabelUrl: label.LabelUrl,
                TrackingNumber: label.TrackingNumber,
                Order: Map(order)
            )
        );
    }

    private static (bool Warning, double? VariancePct) ComputeWeightWarning(
        int? expectedWeightTotal,
        int actualWeightTotal
    )
    {
        if (!expectedWeightTotal.HasValue || expectedWeightTotal.Value == 0)
        {
            return (false, null);
        }
        var signedDelta = (double)(actualWeightTotal - expectedWeightTotal.Value);
        var variancePct = signedDelta / expectedWeightTotal.Value * 100.0;
        var warning = Math.Abs(variancePct) > WeightWarningThreshold * 100.0;
        return (warning, variancePct);
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
