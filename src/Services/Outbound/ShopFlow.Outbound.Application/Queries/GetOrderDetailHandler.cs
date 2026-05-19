using MediatR;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// MediatR handler for <see cref="GetOrderDetailQuery"/> — Sprint-7 plan U3 / R3.
/// Loads the <c>Order</c> aggregate via the existing repository
/// <c>FindByIdAsync</c> port, then layers the saga's current state on top
/// via <see cref="IOrderRepository.GetCurrentSagaStateAsync"/>. Returns
/// <c>Result.Failure("order.not_found", ...)</c> when no order matches the id.
/// </summary>
public sealed class GetOrderDetailHandler
    : IRequestHandler<GetOrderDetailQuery, Result<OrderDetailReadModel>>
{
    private readonly IOrderRepository _orderRepo;

    public GetOrderDetailHandler(IOrderRepository orderRepo)
    {
        _orderRepo = orderRepo;
    }

    public async Task<Result<OrderDetailReadModel>> Handle(
        GetOrderDetailQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await _orderRepo
            .FindByIdAsync(request.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result<OrderDetailReadModel>.Failure(
                $"order {request.OrderId} not found.",
                "order.not_found");
        }

        var currentSagaState = await _orderRepo
            .GetCurrentSagaStateAsync(request.OrderId, cancellationToken)
            .ConfigureAwait(false);

        var lines = order.Lines
            .Select(l => new OrderLineReadModel(
                Id: l.Id,
                Sku: l.Sku,
                Qty: l.Qty,
                ExpectedWeight: l.ExpectedWeight))
            .ToList();

        var detail = new OrderDetailReadModel(
            Id: order.Id,
            ChannelExternalOrderId: order.ChannelExternalOrderId,
            Channel: ListOrdersHandler.ParseChannel(order.ChannelExternalOrderId),
            ShippingProfile: order.ShippingProfile,
            Status: order.Status.ToString(),
            CurrentSagaState: currentSagaState,
            ExpectedWeightTotal: order.ExpectedWeightTotal,
            ActualWeightTotal: order.ActualWeightTotal,
            LabelUrl: order.LabelUrl,
            TrackingNumber: order.TrackingNumber,
            PickWaveId: order.PickWaveId,
            CreatedAt: order.CreatedAt,
            UpdatedAt: order.UpdatedAt,
            Lines: lines);

        return Result<OrderDetailReadModel>.Success(detail);
    }
}
