using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.Inbound.Api.Controllers;

/// <summary>
/// Placeholder controller for the Inbound module (plan U9). Every action
/// returns <c>501 Not Implemented</c>; real endpoints land in Phase-1+.
/// </summary>
[ApiController]
[Route("api/inbound")]
public sealed class InboundController : ControllerBase
{
    [HttpGet]
    public IActionResult Index() => StatusCode(StatusCodes.Status501NotImplemented);
}
