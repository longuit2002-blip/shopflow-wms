using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Channel.Application.Adapters;
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
    private readonly IChannelAdapterFactory _adapterFactory;
    private readonly RequestContext _requestContext;

    // NOTE: WebhookOrchestrator is intentionally NOT constructor-injected.
    // Its dependency graph (IngestWebhookService → IChannelOutbox /
    // IWebhookEventRepository → scoped ChannelDbContext via
    // PerRequestDbContextFactory) reads IRequestContext.DbConnectionString
    // at construction time. MVC activates the controller's ctor deps BEFORE
    // the action runs, but RequestContext.Bind only happens mid-action
    // (Step 4, after HMAC clears). Eager injection therefore threw
    // "tenant scope accessed before the request boundary populated it" the
    // first time this receiver ran through the WAF (a never-run composition
    // gap). Resolving the orchestrator from HttpContext.RequestServices
    // AFTER Bind keeps the scoped DbContext bound to the right tenant DB.
    public WebhooksController(
        IChannelDirectory channelDirectory,
        ITenantCatalog tenantCatalog,
        ISignatureVerifierFactory verifierFactory,
        IChannelAdapterFactory adapterFactory,
        RequestContext requestContext
    )
    {
        _channelDirectory = channelDirectory;
        _tenantCatalog = tenantCatalog;
        _verifierFactory = verifierFactory;
        _adapterFactory = adapterFactory;
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
        CancellationToken ct
    )
    {
        // Step 1: resolve channel -> tenant binding via control-plane catalog.
        var binding = await _channelDirectory.LookupAsync(channelId, ct).ConfigureAwait(false);
        if (binding is null)
        {
            return NotFound(new { error = "unknown channel" });
        }

        if (!string.Equals(binding.ChannelType, channelType, StringComparison.OrdinalIgnoreCase))
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
            // No verifier registered for this channel type — surface loudly.
            // Adding a marketplace (Lazada in finish-line U7) is a pure DI
            // add; this 501 only fires for channel types with no verifier.
            return StatusCode(
                StatusCodes.Status501NotImplemented,
                new { error = $"no verifier registered for channel type '{binding.ChannelType}'" }
            );
        }

        // K8 — channel-agnostic signature extraction. Each marketplace names
        // its signature header differently (X-Shopee-Signature /
        // X-Lazada-Signature); the verifier owns the header name so the
        // controller stays marketplace-agnostic. Read the header the resolved
        // verifier expects rather than a hard-coded X-Shopee-Signature.
        var headers = BuildHeaderSnapshot();
        var signature = headers.TryGetValue(verifier.SignatureHeaderName, out var sig) ? sig : null;

        if (
            string.IsNullOrWhiteSpace(signature)
            || !verifier.Verify(bodyBytes, signature, binding.SecretEncrypted)
        )
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

        // Step 5: parse via the channel-type-specific adapter (Sprint-4.5 U2 —
        // replaces the Sprint-4 body-hash stub). Adapter returns the real
        // marketplace-asserted provider_event_id so replays of semantically
        // identical events (e.g., re-signed retries) collide on UNIQUE.
        var adapter = _adapterFactory.TryResolve(binding.ChannelType);
        if (adapter is null)
        {
            return StatusCode(
                StatusCodes.Status501NotImplemented,
                new { error = $"no adapter registered for channel type '{binding.ChannelType}'" }
            );
        }

        var envelopeResult = adapter.ParseWebhook(channelId, bodyBytes, headers);
        if (!envelopeResult.IsSuccess)
        {
            return BadRequest(
                new { error = envelopeResult.Error, code = envelopeResult.ErrorCode }
            );
        }
        var envelope = envelopeResult.Value!;

        // Step 6: orchestrator owns event-type gating + ParseOrderCreated +
        // per-line mapping resolution + OrderImportedV1 assembly + the
        // fail-whole-import path. Sprint-4.5 U3 — produces a WebhookProcessOutcome
        // that maps cleanly onto the 200-shape responses below.
        //
        // Resolve the orchestrator NOW (post-Bind) from the request scope —
        // its scoped ChannelDbContext binds to the tenant DB resolved above.
        // See the ctor note on why this is not constructor-injected.
        var orchestrator = HttpContext.RequestServices.GetRequiredService<WebhookOrchestrator>();
        var processResult = await orchestrator
            .ProcessAsync(envelope, binding.ChannelType, binding.TenantId, ct)
            .ConfigureAwait(false);

        if (!processResult.IsSuccess)
        {
            return BadRequest(new { error = processResult.Error, code = processResult.ErrorCode });
        }

        var outcome = processResult.Value!;
        return outcome.Status switch
        {
            WebhookProcessStatus.OrderImported => Ok(
                new
                {
                    eventId = outcome.EventId,
                    isDuplicate = outcome.IsDuplicate,
                    status = "order_imported",
                }
            ),
            WebhookProcessStatus.ImportFailed => Ok(
                new
                {
                    eventId = outcome.EventId,
                    isDuplicate = outcome.IsDuplicate,
                    status = "import_failed",
                    reason = "unmapped_skus",
                    unmapped = outcome.UnmappedSkus,
                }
            ),
            WebhookProcessStatus.EventSkipped => Ok(
                new
                {
                    eventId = outcome.EventId,
                    isDuplicate = outcome.IsDuplicate,
                    status = "no_downstream",
                    eventType = envelope.EventType,
                }
            ),
            _ => Ok(new { eventId = outcome.EventId, isDuplicate = outcome.IsDuplicate }),
        };
    }

    /// <summary>
    /// Snapshot the inbound request headers as an immutable case-insensitive
    /// dictionary for the adapter parser. Multi-valued headers collapse to
    /// the most-recent value — Sprint-4.5 adapters do not yet read multi-
    /// valued headers; if they grow to, the contract can widen.
    /// </summary>
    private IReadOnlyDictionary<string, string> BuildHeaderSnapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Request.Headers)
        {
            if (header.Value.Count > 0)
            {
                snapshot[header.Key] = header.Value.ToString();
            }
        }
        return snapshot;
    }
}
