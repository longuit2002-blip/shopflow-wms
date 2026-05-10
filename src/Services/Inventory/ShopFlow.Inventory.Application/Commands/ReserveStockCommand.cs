using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Reserve <paramref name="Quantity"/> units of <paramref name="Sku"/> for
/// <paramref name="OrderId"/>. Idempotent on <c>(tenant_id, order_id)</c>:
/// re-issuing with the same OrderId returns the existing reservation id
/// rather than appending a second row.
/// </summary>
public sealed record ReserveStockCommand(Guid OrderId, string Sku, int Quantity)
    : IRequest<Result<Guid>>;
