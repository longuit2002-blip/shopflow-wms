using Microsoft.AspNetCore.Mvc;
using ShopFlow.SharedKernel.Application;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.Api.Controllers;

/// <summary>
/// Admin surface for the per-SKU <c>is_flash_sale</c> flag consumed by
/// the StockSync engine's priority-queue routing (Sprint-5 plan R10 / U7).
/// </summary>
/// <remarks>
/// <para>Sprint-5 ships this as an unauthenticated stub — every request
/// that survives <c>TenantRoutingMiddleware</c> (Phase-0-redux U4) is
/// trusted. Real admin-API auth lands in Phase-3 alongside the operator
/// console.</para>
///
/// <para>The controller pulls the tenant id from
/// <see cref="IRequestContext.TenantId"/> rather than reading it off the
/// route — the tenant is already resolved by the middleware
/// (header &gt; JWT &gt; subdomain). Tenant isolation is therefore
/// enforced at the same seam that the rest of the module trusts.</para>
/// </remarks>
[ApiController]
[Route("api/skus")]
public sealed class SkuFlagsController : ControllerBase
{
    private readonly ISkuFlagRepository _repo;
    private readonly IRequestContext _requestContext;

    public SkuFlagsController(ISkuFlagRepository repo, IRequestContext requestContext)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(requestContext);
        _repo = repo;
        _requestContext = requestContext;
    }

    public sealed record SetFlagRequest(bool IsFlashSale);

    /// <summary>
    /// Idempotent set of the per-SKU flash-sale flag. Returns 204 on
    /// success. Duplicate writes are no-ops at the domain (aggregate's
    /// <c>SetFlashSale</c> short-circuits when the value is unchanged).
    /// </summary>
    [HttpPut("{sku}/flag")]
    public async Task<IActionResult> SetFlag(
        [FromRoute] string sku,
        [FromBody] SetFlagRequest body,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return BadRequest(new { error = "sku must be non-empty", code = "stocksync.sku.empty" });
        }

        if (body is null)
        {
            return BadRequest(new { error = "body required", code = "stocksync.body.required" });
        }

        await _repo
            .SetFlashSaleAsync(_requestContext.TenantId, sku, body.IsFlashSale, ct)
            .ConfigureAwait(false);

        return NoContent();
    }
}
