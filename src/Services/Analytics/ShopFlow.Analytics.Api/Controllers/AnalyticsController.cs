using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.Analytics.Api.Controllers;

/// <summary>
/// Placeholder controller for the Analytics module (plan U9). Every action
/// returns <c>501 Not Implemented</c>; real endpoints land in Phase-1+.
/// </summary>
[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    [HttpGet]
    public IActionResult Index() => StatusCode(StatusCodes.Status501NotImplemented);
}
