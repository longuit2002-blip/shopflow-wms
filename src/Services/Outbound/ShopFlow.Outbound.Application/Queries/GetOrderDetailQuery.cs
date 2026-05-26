using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// MediatR query for the Sprint-7 Orders detail page (plan U3, R3). Returns
/// the full <c>Order</c> aggregate + its order lines + the current saga
/// state string. The handler delegates to
/// <c>IOrderRepository.FindByIdAsync</c> + <c>GetCurrentSagaStateAsync</c>
/// so all DB access stays repository-mediated per AGENTS.md §3.16.
/// </summary>
/// <param name="OrderId">Order id (uuid).</param>
public sealed record GetOrderDetailQuery(Guid OrderId) : IRequest<Result<OrderDetailReadModel>>;

/// <summary>
/// Read model for <see cref="GetOrderDetailQuery"/>. Carries enough fields to
/// render the detail page header + the line table; the controller's DTO in
/// U4 reshapes this into the wire response.
/// </summary>
/// <param name="Id">Order id.</param>
/// <param name="ChannelExternalOrderId">Channel-side reference.</param>
/// <param name="Channel">Display label parsed from the channel prefix.</param>
/// <param name="ShippingProfile">Carrier / service profile string captured at order create.</param>
/// <param name="Status">Domain <c>Order.Status</c> value (string-converted by EF).</param>
/// <param name="CurrentSagaState">
/// Current saga state from <c>saga_state.CurrentState</c>; may diverge from
/// <see cref="Status"/> by one transition (the saga state lands first; the
/// order row catches up via its own commit).
/// </param>
/// <param name="ExpectedWeightTotal">Sum of <c>line.qty * line.expected_weight</c>; null when any line lacks a weight.</param>
/// <param name="ActualWeightTotal">Weight reported at confirm-pack; null before then.</param>
/// <param name="LabelUrl">Carrier label URL recorded on Shipped transition; null before then.</param>
/// <param name="TrackingNumber">Tracking number recorded on Shipped transition; null before then.</param>
/// <param name="PickWaveId">Pick wave the order was bundled into; null pre-wave-attach.</param>
/// <param name="CreatedAt">When <c>orders.created_at</c> was stamped.</param>
/// <param name="UpdatedAt">Last status-transition wall-clock.</param>
/// <param name="Lines">Materialised <see cref="OrderLineReadModel"/> rows.</param>
public sealed record OrderDetailReadModel(
    Guid Id,
    string ChannelExternalOrderId,
    string Channel,
    string ShippingProfile,
    string Status,
    string? CurrentSagaState,
    int? ExpectedWeightTotal,
    int? ActualWeightTotal,
    string? LabelUrl,
    string? TrackingNumber,
    Guid? PickWaveId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<OrderLineReadModel> Lines
);

/// <summary>
/// One row in <see cref="OrderDetailReadModel.Lines"/>. Mirrors the
/// <c>order_lines</c> shape per Sprint-3-redux R2.
/// </summary>
/// <param name="Id">Order-line id (the K10/K11 composite-UNIQUE token on the Inventory ledger).</param>
/// <param name="Sku">SKU reserved on this line.</param>
/// <param name="Qty">Reservation quantity.</param>
/// <param name="ExpectedWeight">Per-unit expected weight; null when not declared.</param>
public sealed record OrderLineReadModel(Guid Id, string Sku, int Qty, int? ExpectedWeight);
