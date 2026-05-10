using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// Inventory HTTP surface. Endpoints are thin pass-throughs to MediatR:
/// validation, tenant resolution, idempotency, and outbox flush all live
/// downstream. Per AGENTS.md §3.16 this controller never touches a
/// <c>DbSet&lt;T&gt;</c> directly.
/// </summary>
[ApiController]
[Authorize]
[Route("api/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Reserve stock for an order. Idempotent on
    /// <c>(tenant_id, order_id)</c>; concurrent oversell returns 409.
    /// </summary>
    [HttpPost("reservations")]
    public async Task<IActionResult> Reserve(
        [FromBody] ReserveStockRequest request,
        CancellationToken cancellationToken
    )
    {
        var command = new ReserveStockCommand(request.OrderId, request.Sku, request.Quantity);
        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return CreatedAtAction(
                nameof(Reserve),
                new { id = result.Value },
                new { reservationId = result.Value }
            );
        }

        return result.ErrorCode switch
        {
            "OVERSOLD" => Conflict(new { error = result.Error, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.Error, code = result.ErrorCode }),
        };
    }

    /// <summary>
    /// Apply a manual stock adjustment to a SKU.
    /// </summary>
    [HttpPost("stock/{sku}/adjustments")]
    public async Task<IActionResult> Adjust(
        [FromRoute] string sku,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken
    )
    {
        var command = new AdjustStockCommand(sku, request.Delta, request.Reason, request.UserId);
        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Ok();
        }

        return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(new { error = result.Error, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.Error, code = result.ErrorCode }),
        };
    }

    /// <summary>
    /// Read current availability for a SKU
    /// (<c>total − allocated − active reservations</c>).
    /// </summary>
    [HttpGet("stock/{sku}/availability")]
    public async Task<IActionResult> GetAvailability(
        [FromRoute] string sku,
        CancellationToken cancellationToken
    )
    {
        var query = new GetAvailabilityQuery(sku);
        var result = await _mediator.Send(query, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(new { error = result.Error, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.Error, code = result.ErrorCode }),
        };
    }
}

/// <summary>POST body for reservation creation.</summary>
public sealed record ReserveStockRequest(Guid OrderId, string Sku, int Quantity);

/// <summary>POST body for stock adjustment.</summary>
public sealed record AdjustStockRequest(int Delta, StockAdjustmentReason Reason, Guid UserId);
