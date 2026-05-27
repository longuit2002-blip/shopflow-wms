using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Aggregate root for one customer order being fulfilled through the
/// Reserve → Pick → Pack → Ship pipeline per Sprint-3-redux plan R1-R3.
/// Carries the lifecycle <see cref="Status"/> mirror of the fulfillment
/// saga, the optional shipping label / tracking metadata recorded at
/// the carrier call (U6), and N <see cref="OrderLine"/> children each
/// reserving stock under a per-line id (composite UNIQUE on the
/// Inventory ledger per K10/K11).
/// </summary>
/// <remarks>
/// <para>U2 ships the <see cref="Create"/> factory + the saga-driven
/// state-machine methods. U4's <c>FulfillmentSaga</c> invokes the
/// state transitions on the persisted aggregate as the saga progresses
/// through its own state machine. Mirrors the Sprint-2-redux Inbound
/// <c>PurchaseOrder</c> shape: every transition returns
/// <see cref="Result"/>; defensive failure on illegal pre-states with
/// <c>order.invalid_state</c>.</para>
///
/// <para>Inherits from <see cref="BaseEntity"/> (not <see cref="AggregateRoot"/>)
/// — mirrors <see cref="ShopFlow.Inventory.Domain.StockItem"/>'s rationale:
/// the inherited <c>byte[] RowVersion</c> on <c>AggregateRoot</c> doesn't
/// match the per-aggregate row-versioning shape we need. Outbound's
/// optimistic-concurrency token (when needed in Phase-2) will go on the
/// saga_state table managed by MassTransit's EF repo, not on Order itself
/// — the saga is the serialization point.</para>
///
/// <para>Per ADR-0003 no <c>tenant_id</c> column — the database identity
/// is the tenant boundary.</para>
/// </remarks>
public sealed class Order : BaseEntity
{
    public string ChannelExternalOrderId { get; private set; } = string.Empty;

    public string ShippingProfile { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; } = OrderStatus.Created;

    public int? ExpectedWeightTotal { get; private set; }

    public int? ActualWeightTotal { get; private set; }

    public string? LabelUrl { get; private set; }

    public string? TrackingNumber { get; private set; }

    public Guid? PickWaveId { get; private set; }

    private readonly List<OrderLine> _lines = new();

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    private Order() { }

    /// <summary>
    /// Build an order in <see cref="OrderStatus.Created"/>. Validates the
    /// channel ref, shipping profile, and lines collection per the U2
    /// requirements. <see cref="ExpectedWeightTotal"/> is the sum of
    /// <c>line.qty * line.expected_weight</c> when every line has a
    /// weight; <see langword="null"/> when any line lacks one.
    /// </summary>
    public static Result<Order> Create(
        string channelExternalOrderId,
        string shippingProfile,
        IEnumerable<(string Sku, int Qty, int? ExpectedWeight)> lines
    )
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (string.IsNullOrWhiteSpace(channelExternalOrderId))
        {
            return Result<Order>.Failure(
                "channel_external_order_id is required.",
                "order.external_id_required"
            );
        }

        if (string.IsNullOrWhiteSpace(shippingProfile))
        {
            return Result<Order>.Failure(
                "shipping_profile is required.",
                "order.shipping_profile_required"
            );
        }

        var lineList = lines.ToList();
        if (lineList.Count == 0)
        {
            return Result<Order>.Failure("order must have at least one line.", "order.no_lines");
        }

        var order = new Order
        {
            ChannelExternalOrderId = channelExternalOrderId.Trim(),
            ShippingProfile = shippingProfile.Trim(),
            Status = OrderStatus.Created,
        };

        foreach (var (sku, qty, expectedWeight) in lineList)
        {
            var lineResult = OrderLine.Create(order.Id, sku, qty, expectedWeight);
            if (!lineResult.IsSuccess)
            {
                return Result<Order>.Failure(lineResult.Error!, lineResult.ErrorCode);
            }
            order._lines.Add(lineResult.Value!);
        }

        // expected_weight_total: null if any line is missing weight; else
        // sum of (qty * weight). Per-line weight is the per-unit weight;
        // total scales with quantity.
        order.ExpectedWeightTotal = order._lines.All(l => l.ExpectedWeight.HasValue)
            ? order._lines.Sum(l => l.Qty * l.ExpectedWeight!.Value)
            : null;

        return Result<Order>.Success(order);
    }

    /// <summary>
    /// Created → AwaitingReservation. Invoked by U4's saga when it
    /// publishes <c>ReserveStockV1</c> to the Inventory module.
    /// </summary>
    public Result MarkAwaitingReservation() =>
        TransitionFrom(OrderStatus.Created, OrderStatus.AwaitingReservation);

    /// <summary>
    /// AwaitingReservation → Reserved. Invoked by U4's saga on
    /// <c>StockReservedV1</c>.
    /// </summary>
    public Result MarkReserved() =>
        TransitionFrom(OrderStatus.AwaitingReservation, OrderStatus.Reserved);

    /// <summary>
    /// Reserved → AwaitingPick. Invoked by U4's saga as it enqueues the
    /// order onto the pick queue (U5).
    /// </summary>
    public Result MarkAwaitingPick() =>
        TransitionFrom(OrderStatus.Reserved, OrderStatus.AwaitingPick);

    /// <summary>
    /// AwaitingPick → Picked. Invoked by U6's <c>POST /confirm-pick</c>
    /// endpoint after the picker reports completion.
    /// </summary>
    public Result MarkPicked() => TransitionFrom(OrderStatus.AwaitingPick, OrderStatus.Picked);

    /// <summary>
    /// Picked → AwaitingPack.
    /// </summary>
    public Result MarkAwaitingPack() =>
        TransitionFrom(OrderStatus.Picked, OrderStatus.AwaitingPack);

    /// <summary>
    /// AwaitingPack → Packed. Invoked by U6's <c>POST /confirm-pack</c>
    /// endpoint after the weight check passes. <paramref name="actualWeightTotal"/>
    /// is recorded for reconciliation against <see cref="ExpectedWeightTotal"/>.
    /// </summary>
    public Result MarkPacked(int actualWeightTotal)
    {
        var transition = TransitionFrom(OrderStatus.Picked, OrderStatus.Packed);
        if (!transition.IsSuccess)
        {
            return transition;
        }
        ActualWeightTotal = actualWeightTotal;
        return Result.Success();
    }

    /// <summary>
    /// Packed → AwaitingShip.
    /// </summary>
    public Result MarkAwaitingShip() =>
        TransitionFrom(OrderStatus.Packed, OrderStatus.AwaitingShip);

    /// <summary>
    /// AwaitingShip → Shipped. Records the carrier label + tracking number
    /// returned by the mock shipping provider (U6).
    /// </summary>
    public Result MarkShipped(string labelUrl, string trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(labelUrl))
        {
            return Result.Failure("label_url is required.", "order.label_url_required");
        }
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return Result.Failure("tracking_number is required.", "order.tracking_number_required");
        }
        var transition = TransitionFrom(OrderStatus.AwaitingShip, OrderStatus.Shipped);
        if (!transition.IsSuccess)
        {
            return transition;
        }
        LabelUrl = labelUrl.Trim();
        TrackingNumber = trackingNumber.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Reserved OR AwaitingPick OR AwaitingShip → CompensatingReservation.
    /// Entry point for the saga's compensation paths. The saga publishes
    /// <c>ReleaseStockV1</c> from this state. Three callers:
    /// </summary>
    /// <remarks>
    /// <para><c>Reserved</c> pre-state is the legacy hook from the
    /// <c>StockReservationFailedV1</c> race in <c>AwaitingReservation</c>
    /// — kept for completeness even though that path now flows through
    /// the saga only (Order stays in <c>AwaitingReservation</c> when the
    /// atomic-CTE failure arrives; U7's saga path A short-circuits the
    /// CompensatingReservation state and drives the Order to
    /// <c>Cancelled</c> via the in-process <c>OrderCancelled</c> event).</para>
    ///
    /// <para><c>AwaitingPick</c> pre-state is the Sprint-3-redux U7 pick-
    /// failure path: <c>POST /mark-pick-failed</c> calls this method to
    /// record the Order's compensating intent BEFORE publishing the saga's
    /// <c>PickFailed</c> event. The Order stays in
    /// <c>CompensatingReservation</c> until the saga's compensation
    /// completes and the <c>OrderCancelled</c> consumer flips it to
    /// <c>Cancelled</c> (R3 eventual-consistency boundary).</para>
    ///
    /// <para><c>Picked</c> pre-state is the Sprint-13 U3 pack-failure path:
    /// <c>POST /mark-pack-failed</c> calls this method when the Packer
    /// discovers a damaged item at the pack station AFTER pick-confirm but
    /// BEFORE pack-confirm. Per Sprint-13 K1 (BLOCKING factual correction
    /// over the brainstorm), the Order aggregate is in <c>Picked</c> — NOT
    /// <c>AwaitingPack</c> — at this moment: <c>ConfirmPackAsync</c> chains
    /// <c>MarkPacked → MarkAwaitingShip</c> atomically, so the aggregate
    /// never sits at rest in <c>AwaitingPack</c>. The saga is also in
    /// <c>Picked</c>; its <c>During(Picked, When(PackFailed))</c> Path D
    /// clause reuses the Sprint-3-redux Path B / Sprint-12.5 Path C
    /// compensation primitives unchanged (<c>ReservedLineSkus</c> +
    /// <c>LinesAwaitingRelease</c> survive through <c>Picked</c>).</para>
    ///
    /// <para><c>AwaitingShip</c> pre-state is the Sprint-12.5 U3 ship-
    /// failure path: <c>POST /mark-ship-failed</c> calls this method when
    /// the carrier rejects the label or the package is damaged pre-ship.
    /// Per Sprint-12 KTD2, by the time mark-ship-failed fires, the Order
    /// aggregate has already moved Packed → AwaitingShip via
    /// <c>ConfirmPackAsync</c>'s chain (the saga state is still Packed —
    /// these two state machines run one step apart by design). The saga's
    /// Packed → CompensatingReservation Path C reuses the Sprint-3-redux
    /// Path B compensation primitives unchanged
    /// (<c>ReservedLineSkus</c> + <c>LinesAwaitingRelease</c>).</para>
    /// </remarks>
    public Result MarkCompensatingReservation()
    {
        if (
            Status != OrderStatus.Reserved
            && Status != OrderStatus.AwaitingPick
            && Status != OrderStatus.Picked
            && Status != OrderStatus.AwaitingShip
        )
        {
            return Result.Failure(
                $"cannot transition from {Status} to CompensatingReservation; required pre-state Reserved, AwaitingPick, Picked, or AwaitingShip.",
                "order.invalid_state"
            );
        }
        Status = OrderStatus.CompensatingReservation;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// CompensatingReservation or AwaitingReservation → Cancelled.
    /// Terminal state after compensation completes or before reservation
    /// even succeeded.
    /// </summary>
    public Result MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return Result.Failure("already cancelled.", "order.already_cancelled");
        }
        if (
            Status != OrderStatus.CompensatingReservation
            && Status != OrderStatus.AwaitingReservation
            && Status != OrderStatus.Created
        )
        {
            return Result.Failure($"cannot cancel order in {Status} state.", "order.invalid_state");
        }
        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Associate the order with a pick wave (U5's wave generator
    /// populates this when the order is bundled into a wave). Nullable
    /// because <c>Created</c> orders have no wave yet.
    /// </summary>
    public Result AttachToPickWave(Guid pickWaveId)
    {
        if (pickWaveId == Guid.Empty)
        {
            return Result.Failure("pick_wave_id is required.", "order.pick_wave_id_required");
        }
        PickWaveId = pickWaveId;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    private Result TransitionFrom(OrderStatus required, OrderStatus next)
    {
        if (Status != required)
        {
            return Result.Failure(
                $"cannot transition from {Status} to {next}; required pre-state {required}.",
                "order.invalid_state"
            );
        }
        Status = next;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
