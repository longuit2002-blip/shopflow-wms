using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.ControlPlane.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Channel.Api.Controllers;

/// <summary>
/// Public webhook receiver for marketplace events per Sprint-4 plan R3/R4
/// + U3. Bypasses <see cref="TenantRoutingMiddleware"/> via
/// <see cref="SkipTenantRoutingAttribute"/> because inbound requests carry
/// no <c>X-ShopFlow-Tenant</c> header — tenant identity is resolved from
/// <c>channel_id</c> via <see cref="IChannelDirectory"/> after HMAC
/// verification.
/// </summary>
/// <remarks>
/// <para>Flow:</para>
/// <list type="number">
///   <item><description>Resolve <c>channel_id → ChannelTenantBinding</c>. 404 on unknown channel.</description></item>
///   <item><description>Verify HMAC via the channel-type-specific <see cref="ISignatureVerifier"/>. 401 on mismatch — NO DB write.</description></item>
///   <item><description>Resolve the tenant via <see cref="ITenantCatalog"/> and bind <see cref="RequestContext"/>.</description></item>
///   <item><description>Adapter-parse the body into <see cref="WebhookEnvelope"/> (Sprint-4 U5 ships the adapter; U3 ships a stub envelope from raw payload).</description></item>
///   <item><description>Call <see cref="IngestWebhookService.IngestAsync"/> — idempotent UNIQUE-23505 insert + first-write-only outbox append.</description></item>
///   <item><description>Return 200 with the event id + <c>isDuplicate</c> flag.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/channel/webhooks")]
[AllowAnonymous]
[SkipTenantRouting]
public sealed class WebhooksController : ControllerBase
{
    private readonly IChannelDirectory _channelDirectory;
    private readonly ITenantCatalog _tenantCatalog;
    private readonly ISignatureVerifierFactory _verifierFactory;
    private readonly IngestWebhookService _ingest;
    private readonly RequestContext _requestContext;

    public WebhooksController(
        IChannelDirectory channelDirectory,
        ITenantCatalog tenantCatalog,
        ISignatureVerifierFactory verifierFactory,
        IngestWebhookService ingest,
        RequestContext requestContext
    )
    {
        _channelDirectory = channelDirectory;
        _tenantCatalog = tenantCatalog;
        _verifierFactory = verifierFactory;
        _ingest = ingest;
        _requestContext = requestContext;
    }

    /// <summary>
    /// Receive one marketplace webhook. The U3 baseline uses a placeholder
    /// downstream event-type string + the raw envelope object as payload;
    /// U8 substitutes <c>OrderImportedV1.AssemblyQualifiedName</c> + the
    /// constructed contract instance after adapter parsing (U5).
    /// </summary>
    [HttpPost("{channelType}/{channelId:guid}")]
    public async Task<IActionResult> Receive(
        [FromRoute] string channelType,
        [FromRoute] Guid channelId,
        [FromHeader(Name = "X-Shopee-Signature")] string? signature,
        CancellationToken ct
    )
    {
        // Step 1: resolve channel -> tenant binding via control-plane catalog.
        var binding = await _channelDirectory.LookupAsync(channelId, ct).ConfigureAwait(false);
        if (binding is null)
        {
            return NotFound(new { error = "unknown channel" });
        }

        if (!string.Equals(
            binding.ChannelType,
            channelType,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            return BadRequest(new { error = "channel type mismatch" });
        }

        // Step 2: read raw body for HMAC verification. Body buffering must
        // be enabled by middleware before this controller runs (see
        // Channel.Api Program.cs U9).
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bodyBytes = ms.ToArray();

        // Step 3: HMAC verification via channel-type-specific verifier.
        var verifier = _verifierFactory.Resolve(binding.ChannelType);
        if (verifier is null)
        {
            // No verifier registered for this channel type — Sprint-6+ will
            // add Lazada/TikTok adapters. Surface loudly.
            return StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = $"no verifier registered for channel type '{binding.ChannelType}'",
            });
        }

        if (string.IsNullOrWhiteSpace(signature)
            || !verifier.Verify(bodyBytes, signature, binding.SecretEncrypted))
        {
            return Unauthorized(new { error = "signature verification failed" });
        }

        // Step 4: bind tenant context now that HMAC has cleared.
        var tenant = await _tenantCatalog
            .LookupByIdAsync(binding.TenantId, ct)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return NotFound(new { error = "tenant not found" });
        }
        _requestContext.Bind(tenant, HttpContext.TraceIdentifier, userId: null);

        // Step 5: stub envelope (U5 ships the Shopee adapter that produces
        // the real envelope from bodyBytes). For U3 we pass the raw bytes
        // through with a placeholder provider_event_id derived from the
        // body hash — tests exercise the idempotency invariant either way.
        var bodyText = Encoding.UTF8.GetString(bodyBytes);
        var envelope = new WebhookEnvelope(
            ChannelId: channelId,
            ProviderEventId: ExtractProviderEventIdStub(bodyText, signature),
            EventType: "webhook.raw",
            RawPayload: bodyText,
            OccurredAt: DateTime.UtcNow
        );

        // Step 6: orchestrator handles UNIQUE-23505 + first-write outbox.
        // U3 ships a placeholder downstream event type; U8 swaps in
        // OrderImportedV1.AssemblyQualifiedName + the parsed contract.
        var ingestResult = await _ingest
            .IngestAsync(
                envelope,
                downstreamEventType: "ShopFlow.Channel.Webhooks.WebhookReceivedV1",
                downstreamPayload: envelope,
                ct
            )
            .ConfigureAwait(false);

        if (!ingestResult.IsSuccess)
        {
            return BadRequest(new { error = ingestResult.Error, code = ingestResult.ErrorCode });
        }

        return Ok(new
        {
            eventId = ingestResult.Value!.EventId,
            isDuplicate = ingestResult.Value!.IsDuplicate,
        });
    }

    /// <summary>
    /// Sprint-4 U3 stub: derive a per-webhook idempotency token from the
    /// (body, signature) pair so the receiver's UNIQUE constraint still
    /// catches replays even before the U5 Shopee adapter parses a real
    /// <c>provider_event_id</c> out of the body. Sprint-4 U5 replaces this
    /// with <c>ShopeeWebhookParser.Parse(bodyBytes).Value.ProviderEventId</c>.
    /// </summary>
    private static string ExtractProviderEventIdStub(string body, string? signature)
    {
        // Combine body + signature so replays of the same signed payload
        // collide on UNIQUE; different signatures (e.g., post-secret-rotation)
        // count as different events.
        var combined = $"{signature}-{body.GetHashCode(StringComparison.Ordinal):x}-{body.Length}";
        return combined[..Math.Min(combined.Length, 64)];
    }
}
