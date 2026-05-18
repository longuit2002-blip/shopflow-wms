using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inventory.Application.Commands;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// POST <c>/api/v1/inventory/adjustments</c> — apply a delta to one SKU
/// (Sprint-6 plan U8 / R8). Emits a <c>StockLevelChangedV1</c> outbox
/// message via the Sprint-5 U2 path so downstream StockSync replays
/// the change to channels.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/inventory/adjustments")]
public sealed class AdjustmentsController(IMediator mediator) : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";
    private readonly IMediator mediator = mediator;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Adjust(
        [FromBody] AdjustmentRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null) return this.ValidationProblem("request body is required.");

        var idem = this.Request.Headers[IdempotencyHeader].ToString();
        var result = await this.mediator.Send(
            new AdjustStockCommand(body.Sku, body.Delta, body.Reason, body.Note, idem),
            cancellationToken);

        if (result.IsSuccess) return this.NoContent();
        if (result.ErrorCode == "stock.sku_not_found")
        {
            return this.Problem(
                title: "SKU not found",
                detail: result.Error,
                statusCode: StatusCodes.Status404NotFound);
        }
        return this.ValidationProblem(result.Error);
    }
}

public sealed record AdjustmentRequest(string Sku, int Delta, string Reason, string? Note);
