using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// Read + write endpoints under <c>/api/v1/inventory/skus</c> for the
/// Inventory screen's SKU table, ledger drawer (U7), create-SKU modal
/// (U8 / R11), and threshold/flash-sale inline edits (U8 / R9 + R10).
///
/// All routes require a valid JWT carrying a <c>tenant_slug</c> claim;
/// <c>UseTenantRouting</c> binds the per-request DbContext to the matching
/// tenant database before MediatR handlers run.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/inventory/skus")]
public sealed class SkusController(IMediator mediator) : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";
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
    /// GET /api/v1/inventory/skus/{sku}/ledger?limit=&cursor=
    ///
    /// Sprint-7.5 U6: opaque base64 cursor pagination. Default page size 50
    /// (was 100); clamps to [1, 200]. Returns <c>nextCursor</c> non-null
    /// when more rows remain.
    /// </summary>
    [HttpGet("{sku}/ledger")]
    [ProducesResponseType(typeof(SkuLedgerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SkuLedgerDto>> Ledger(
        string sku,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return this.ValidationProblem("sku is required.");
        }
        if (!string.IsNullOrEmpty(cursor) &&
            ShopFlow.Inventory.Infrastructure.Pagination.OpaqueCursor.TryDecode(cursor) is null)
        {
            return this.Problem(
                title: "Invalid cursor",
                detail: "ledger.cursor_invalid",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var result = await this.mediator.Send(new GetSkuLedgerQuery(sku, limit, cursor), cancellationToken);
        return this.Ok(result);
    }

    /// <summary>
    /// POST /api/v1/inventory/skus — create a new SKU (R11 / Sprint-6 U8 / U12).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Create(
        [FromBody] CreateSkuRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return this.ValidationProblem("request body is required.");
        }
        var idem = this.Request.Headers[IdempotencyHeader].ToString();
        var result = await this.mediator.Send(
            new CreateSkuCommand(body.Sku, body.InitialAvailable, idem),
            cancellationToken);

        if (result.IsSuccess)
        {
            return this.Created($"/api/v1/inventory/skus/{body.Sku}", null);
        }
        if (result.ErrorCode == "stock.sku_already_exists")
        {
            return this.Problem(
                title: "SKU already exists",
                detail: result.Error,
                statusCode: StatusCodes.Status409Conflict);
        }
        return this.ValidationProblem(result.Error);
    }

    /// <summary>
    /// PUT /api/v1/inventory/skus/{sku}/threshold — set low-stock threshold
    /// (R9 / Sprint-6 U8). Sprint-6 stores in-memory; Sprint-7 promotes to
    /// a real <c>stock_items.threshold</c> column.
    /// </summary>
    [HttpPut("{sku}/threshold")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SetThreshold(
        string sku,
        [FromBody] SetThresholdRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null) return this.ValidationProblem("request body is required.");
        var idem = this.Request.Headers[IdempotencyHeader].ToString();
        var result = await this.mediator.Send(
            new SetThresholdCommand(sku, body.Threshold, idem),
            cancellationToken);
        return result.IsSuccess ? this.NoContent() : this.ValidationProblem(result.Error);
    }

    /// <summary>
    /// PUT /api/v1/inventory/skus/{sku}/flash-sale — toggle is_flash_sale
    /// (R10 / Sprint-6 U12). Same in-memory caveat as threshold.
    /// </summary>
    [HttpPut("{sku}/flash-sale")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SetFlashSale(
        string sku,
        [FromBody] SetFlashSaleRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null) return this.ValidationProblem("request body is required.");
        var idem = this.Request.Headers[IdempotencyHeader].ToString();
        var result = await this.mediator.Send(
            new SetFlashSaleCommand(sku, body.Active, idem),
            cancellationToken);
        return result.IsSuccess ? this.NoContent() : this.ValidationProblem(result.Error);
    }
}

public sealed record CreateSkuRequest(string Sku, int InitialAvailable);
public sealed record SetThresholdRequest(int Threshold);
public sealed record SetFlashSaleRequest(bool Active);
