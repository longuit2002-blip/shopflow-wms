using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// Aggregate read endpoint for the Inventory screen's KPI strip
/// (Sprint-6 plan U7 / R21 Backend Gap closure). Polled at the 2-second
/// cadence the frontend uses for live updates.
///
/// Replaces the previous Phase-0-redux placeholder controller (the
/// `api/inventory/availability/{sku}` + `api/inventory/reservations*`
/// + `api/inventory/adjustments` stubs) which returned 501 and had no
/// callers. Adjustments + reservations move to dedicated controllers
/// under <c>api/v1/inventory/</c> in U8.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/inventory")]
public sealed class InventoryController(IMediator mediator) : ControllerBase
{
    private readonly IMediator mediator = mediator;

    /// <summary>
    /// GET /api/v1/inventory/summary
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(InventorySummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InventorySummaryDto>> Summary(
        CancellationToken cancellationToken = default)
    {
        var result = await this.mediator.Send(new GetInventorySummaryQuery(), cancellationToken);
        return this.Ok(result);
    }
}
