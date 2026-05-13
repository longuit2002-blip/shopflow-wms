using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.Outbound.Api.Controllers;

/// <summary>
/// Operator-facing HTTP surface for the order fulfillment flow per
/// Sprint-3-redux plan R11. U1 ships a stub returning 501 on every
/// endpoint so the controller-routing wire-up is in place + the module
/// shape compiles; U2 fills in <c>POST /orders</c> + <c>GET /orders/{id}</c>
/// and U6/U7 add the pick/pack/ship endpoints.
/// </summary>
[ApiController]
[Route("api/outbound/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create() =>
        Problem(
            statusCode: 501,
            title: "POST /api/outbound/orders ships in Sprint-3-redux U2.",
            type: "https://shopflow.example/errors/not_implemented"
        );

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
    {
        _ = id;
        return Problem(
            statusCode: 501,
            title: "GET /api/outbound/orders/{id} ships in Sprint-3-redux U2.",
            type: "https://shopflow.example/errors/not_implemented"
        );
    }
}
