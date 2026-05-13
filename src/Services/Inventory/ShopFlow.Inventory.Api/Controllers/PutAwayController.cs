using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inventory.Application.Ports;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// Put-away suggestion controller per Sprint-2-redux plan R16-R17.
/// <c>GET /api/inventory/put-away-suggestion?sku=X&qty=N&top=3</c>
/// returns the top-3 (configurable) bin candidates ranked by zone
/// priority + available capacity + occupancy + bin name lex.
/// </summary>
[ApiController]
[Route("api/inventory/put-away-suggestion")]
public sealed class PutAwayController : ControllerBase
{
    private readonly IPutAwaySuggestionService _service;

    public PutAwayController(IPutAwaySuggestionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetSuggestionsAsync(
        [FromQuery] string? sku,
        [FromQuery] int? qty,
        [FromQuery] int top = 3,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "sku is required.",
                type: "https://shopflow.example/errors/put-away.sku_required"
            );
        }
        if (qty is null or <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "qty must be > 0.",
                type: "https://shopflow.example/errors/put-away.qty_non_positive"
            );
        }
        if (top <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "top must be > 0.",
                type: "https://shopflow.example/errors/put-away.top_non_positive"
            );
        }

        var candidates = await _service.GetTopCandidatesAsync(sku, qty.Value, top, ct);
        return Ok(candidates);
    }
}
