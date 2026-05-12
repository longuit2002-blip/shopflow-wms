using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.Channel.Api.Controllers;

/// <summary>
/// Placeholder controller for the Channel module (plan U9). Every action
/// returns <c>501 Not Implemented</c>; real endpoints land in Phase-1+.
/// </summary>
[ApiController]
[Route("api/channel")]
public sealed class ChannelController : ControllerBase
{
    [HttpGet]
    public IActionResult Index() => StatusCode(StatusCodes.Status501NotImplemented);
}
