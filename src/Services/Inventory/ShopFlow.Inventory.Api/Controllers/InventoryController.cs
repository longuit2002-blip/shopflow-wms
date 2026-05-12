using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.Inventory.Api.Controllers;

/// <summary>
/// Placeholder controller for the Inventory module (plan U8). Every action
/// returns <c>501 Not Implemented</c>; Sprint-1-redux (plan 003) wires the
/// real reservation/availability/adjustment endpoints against the
/// flesh-out repositories.
/// </summary>
[ApiController]
[Route("api/inventory")]
public sealed class InventoryController : ControllerBase
{
    [HttpGet("availability/{sku}")]
    public IActionResult GetAvailability(string sku)
    {
        _ = sku;
        return StatusCode(StatusCodes.Status501NotImplemented);
    }

    [HttpPost("reservations")]
    public IActionResult CreateReservation()
    {
        return StatusCode(StatusCodes.Status501NotImplemented);
    }

    [HttpPost("reservations/{orderId}/confirm")]
    public IActionResult ConfirmReservation(string orderId)
    {
        _ = orderId;
        return StatusCode(StatusCodes.Status501NotImplemented);
    }

    [HttpPost("reservations/{orderId}/release")]
    public IActionResult ReleaseReservation(string orderId)
    {
        _ = orderId;
        return StatusCode(StatusCodes.Status501NotImplemented);
    }

    [HttpPost("adjustments")]
    public IActionResult Adjust()
    {
        return StatusCode(StatusCodes.Status501NotImplemented);
    }
}
