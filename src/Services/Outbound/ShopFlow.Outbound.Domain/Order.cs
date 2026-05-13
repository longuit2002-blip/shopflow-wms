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
            return Result<Order>.Failure(
                "order must have at least one line.",
                "order.no_lines"
            );
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
    public Result MarkAwaitingReservation()
        => TransitionFrom(OrderStatus.Created, OrderStatus.AwaitingReservation);

    /// <summary>
    /// AwaitingReservation → Reserved. Invoked by U4's saga on
    /// <c>StockReservedV1</c>.
    /// </summary>
    public Result MarkReserved()
        => TransitionFrom(OrderStatus.AwaitingReservation, OrderStatus.Reserved);

    /// <summary>
    /// Reserved → AwaitingPick. Invoked by U4's saga as it enqueues the
    /// order onto the pick queue (U5).
    /// </summary>
    public Result MarkAwaitingPick()
        => TransitionFrom(OrderStatus.Reserved, OrderStatus.AwaitingPick);

    /// <summary>
    /// AwaitingPick → Picked. Invoked by U6's <c>POST /confirm-pick</c>
    /// endpoint after the picker reports completion.
    /// </summary>
    public Result MarkPicked()
        => TransitionFrom(OrderStatus.AwaitingPick, OrderStatus.Picked);

    /// <summary>
    /// Picked → AwaitingPack.
    /// </summary>
    public Result MarkAwaitingPack()
        => TransitionFrom(OrderStatus.Picked, OrderStatus.AwaitingPack);

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
    public Result MarkAwaitingShip()
        => TransitionFrom(OrderStatus.Packed, OrderStatus.AwaitingShip);

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
            return Result.Failure(
                "tracking_number is required.",
                "order.tracking_number_required"
            );
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
    /// Reserved → CompensatingReservation. Entry point for the saga's
    /// compensation path (U7). The saga publishes <c>ReleaseStockV1</c>
    /// from this state.
    /// </summary>
    public Result MarkCompensatingReservation()
        => TransitionFrom(OrderStatus.Reserved, OrderStatus.CompensatingReservation);

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
            return Result.Failure(
                $"cannot cancel order in {Status} state.",
                "order.invalid_state"
            );
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
