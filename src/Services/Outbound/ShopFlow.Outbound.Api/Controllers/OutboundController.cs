using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.Outbound.Api.Controllers;

/// <summary>
/// Placeholder controller for the Outbound module (plan U9). Every action
/// returns <c>501 Not Implemented</c>; real endpoints land in Phase-1+.
/// </summary>
[ApiController]
[Route("api/outbound")]
public sealed class OutboundController : ControllerBase
{
    [HttpGet]
    public IActionResult Index() => StatusCode(StatusCodes.Status501NotImplemented);
}
