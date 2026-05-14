using Polly;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Infrastructure.Adapters;

/// <summary>
/// Shopee marketplace adapter per Sprint-4 plan R1/U5. Stateless — parses
/// inbound webhooks via <see cref="ShopeeWebhookParser"/>; outbound stock
/// push is a Sprint-5 deferred stub (the body lands alongside the stock
/// sync engine).
/// </summary>
/// <remarks>
/// Outbound HTTP retry wrap (Polly v8 <see cref="ResiliencePipeline"/>) is
/// injected so Sprint-5 can swap the policy without touching the adapter.
/// Mirrors the Sprint-3-redux <c>MockShippingProvider</c> retry pattern.
/// </remarks>
public sealed class ShopeeAdapter : IChannelAdapter
{
    private readonly ShopeeWebhookParser _parser;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly HttpClient _httpClient;

    public ShopeeAdapter(
        ShopeeWebhookParser parser,
        ResiliencePipeline retryPipeline,
        HttpClient httpClient
    )
    {
        _parser = parser;
        _retryPipeline = retryPipeline;
        _httpClient = httpClient;
    }

    public string ChannelType => "shopee";

    public Result<WebhookEnvelope> ParseWebhook(
        Guid channelId,
        ReadOnlySpan<byte> body,
        IReadOnlyDictionary<string, string> headers
    )
    {
        // Headers reserved for Sprint-5+ when Shopee rate-limit-header
        // round-trips into the sync engine's per-channel token bucket.
        // Sprint-4 U5 ignores them at parse time — the receiver passes
        // the raw bytes plus channelId only.
        return _parser.Parse(channelId, body);
    }

    /// <summary>
    /// Sprint-4.5 U1 — extract Shopee's <c>data</c> field into the
    /// marketplace-agnostic <see cref="ExternalOrderDraft"/>. Delegates
    /// to <see cref="ShopeeWebhookParser.ParseOrderData"/>; this method's
    /// job is event-type gating + Result wrapping.
    /// </summary>
    public Result<ExternalOrderDraft> ParseOrderCreated(WebhookEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (
            !string.Equals(
                envelope.EventType,
                "order.created",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return Result<ExternalOrderDraft>.Failure(
                $"shopee.order: event_type '{envelope.EventType}' is not supported by ParseOrderCreated.",
                "shopee.order.event_type_unsupported"
            );
        }

        return _parser.ParseOrderData(envelope.RawPayload);
    }

    public Task<Result> PushStockUpdateAsync(StockUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Sprint-4 U5 stub. Sprint-5 ships the body alongside the sync
        // engine (coalescing buffer + per-channel token bucket + the
        // _retryPipeline + _httpClient call). Polly + HttpClient are
        // injected now so Sprint-5 only needs to fill this method.
        _ = _retryPipeline;
        _ = _httpClient;
        return Task.FromResult(
            Result.Failure(
                "Shopee stock push is deferred to Sprint-5 (stock sync engine).",
                "shopee.push_stock_sprint_5_deferred"
            )
        );
    }
}
