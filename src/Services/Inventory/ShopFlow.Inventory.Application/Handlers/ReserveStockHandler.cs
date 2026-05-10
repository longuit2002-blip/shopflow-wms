using MediatR;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Handlers;

/// <summary>
/// Handles <see cref="ReserveStockCommand"/> per Tech Design §7.7. The
/// handler is intentionally trivial because correctness lives in the
/// reservation-ledger SQL: idempotency short-circuit on
/// <c>(tenant_id, order_id)</c>, then delegate to
/// <see cref="IReservationRepository.TryReserveAsync"/>.
/// </summary>
public sealed class ReserveStockHandler : IRequestHandler<ReserveStockCommand, Result<Guid>>
{
    private readonly IReservationRepository _reservations;
    private readonly IRequestContext _requestContext;

    public ReserveStockHandler(IReservationRepository reservations, IRequestContext requestContext)
    {
        _reservations = reservations;
        _requestContext = requestContext;
    }

    public async Task<Result<Guid>> Handle(
        ReserveStockCommand command,
        CancellationToken cancellationToken
    )
    {
        var tenantId = _requestContext.TenantId;

        var existing = await _reservations
            .FindByOrderIdAsync(tenantId, command.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result<Guid>.Success(existing.Id);
        }

        var sku = new Sku(command.Sku);
        return await _reservations
            .TryReserveAsync(tenantId, sku, command.Quantity, command.OrderId, cancellationToken)
            .ConfigureAwait(false);
    }
}
