using Microsoft.AspNetCore.Mvc;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Domain.ProductMappings;

namespace ShopFlow.Channel.Api.Controllers;

/// <summary>
/// Per-tenant product mapping CRUD + resolve surface per Sprint-4 plan U6.
/// All endpoints flow through <see cref="TenantRoutingMiddleware"/> — the
/// operator must supply <c>X-ShopFlow-Tenant</c> on every request (no
/// channel-routed bypass here; mappings are tenant-scoped admin data).
/// </summary>
[ApiController]
[Route("api/channel/product-mappings")]
public sealed class ProductMappingsController : ControllerBase
{
    private readonly IProductMappingRepository _repo;
    private readonly IProductMappingService _service;

    public ProductMappingsController(
        IProductMappingRepository repo,
        IProductMappingService service
    )
    {
        _repo = repo;
        _service = service;
    }

    public sealed record CreateMappingRequest(Guid ChannelId, string ExternalSku, string InternalSku);
    public sealed record ResolveRequest(Guid ChannelId, string ExternalSku);

    /// <summary>
    /// Admin: upsert a Manual mapping. Idempotent on (channel_id,
    /// external_sku) UNIQUE — duplicate POST returns 200 with the existing
    /// row rather than 409.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateMappingRequest body,
        CancellationToken ct
    )
    {
        if (body is null)
        {
            return BadRequest(new { error = "body required" });
        }

        var skuResult = ExternalSku.Create(body.ExternalSku);
        if (!skuResult.IsSuccess)
        {
            return BadRequest(new { error = skuResult.Error, code = skuResult.ErrorCode });
        }

        var result = await _repo
            .UpsertManualAsync(body.ChannelId, skuResult.Value!, body.InternalSku, ct)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        }

        var mapping = result.Value!;
        return Ok(new
        {
            id = mapping.Id,
            channelId = mapping.ChannelId,
            externalSku = mapping.ExternalSku.Value,
            internalSku = mapping.InternalSku,
            method = mapping.Method.ToString(),
            confidence = mapping.ConfidenceScore,
        });
    }

    /// <summary>
    /// Resolve: (channel_id, external_sku) → internal_sku via the three-tier
    /// service. 404 on unmappable SKU.
    /// </summary>
    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveRequest body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ExternalSku))
        {
            return BadRequest(new { error = "body and external_sku required" });
        }
        var resolution = await _service
            .ResolveAsync(body.ChannelId, body.ExternalSku, ct)
            .ConfigureAwait(false);
        if (resolution is null)
        {
            return NotFound(new { error = "unmapped sku" });
        }
        return Ok(resolution);
    }

    /// <summary>
    /// Paged list per channel for the operator surface (Phase-3 Sprint-7 UI).
    /// </summary>
    [HttpGet("{channelId:guid}")]
    public async Task<IActionResult> List(
        [FromRoute] Guid channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default
    )
    {
        var rows = await _repo
            .ListByChannelAsync(channelId, page, pageSize, ct)
            .ConfigureAwait(false);
        return Ok(rows.Select(r => new
        {
            id = r.Id,
            channelId = r.ChannelId,
            externalSku = r.ExternalSku.Value,
            internalSku = r.InternalSku,
            method = r.Method.ToString(),
            confidence = r.ConfidenceScore,
            createdAt = r.CreatedAt,
        }));
    }
}
