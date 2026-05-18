using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// Read endpoints for the Inventory screen's SKU table + ledger drawer
/// (Sprint-6 plan U7). Both routes require a valid JWT carrying a
/// <c>tenant_slug</c> claim; <c>UseTenantRouting</c> binds the per-request
/// DbContext to the matching tenant database before MediatR handlers run.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/inventory/skus")]
public sealed class SkusController(IMediator mediator) : ControllerBase
{
    private readonly IMediator mediator = mediator;

    /// <summary>
    /// GET /api/v1/inventory/skus?search=&page=&pageSize=
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedSkuListDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedSkuListDto>> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await this.mediator.Send(
            new ListSkusQuery(search, page, pageSize), cancellationToken);
        return this.Ok(result);
    }

    /// <summary>
    /// GET /api/v1/inventory/skus/{sku}/ledger?limit=
    /// </summary>
    [HttpGet("{sku}/ledger")]
    [ProducesResponseType(typeof(SkuLedgerDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SkuLedgerDto>> Ledger(
        string sku,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return this.ValidationProblem("sku is required.");
        }
        var result = await this.mediator.Send(new GetSkuLedgerQuery(sku, limit), cancellationToken);
        return this.Ok(result);
    }
}
