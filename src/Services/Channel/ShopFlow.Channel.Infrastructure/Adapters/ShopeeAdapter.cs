using System.Net;
using System.Net.Http.Json;
using Polly;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Infrastructure.Adapters.Shopee;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Infrastructure.Adapters;

/// <summary>
/// Shopee marketplace adapter per Sprint-4 plan R1/U5 + Sprint-5 plan U6.
/// Stateless — parses inbound webhooks via <see cref="ShopeeWebhookParser"/>
/// and pushes outbound stock updates through a Polly-wrapped
/// <see cref="HttpClient"/> against the Shopee Open Platform v2 API.
/// </summary>
/// <remarks>
/// Outbound HTTP retry wrap (Polly v8 <see cref="ResiliencePipeline"/>) is
/// injected so Sprint-5+ can swap the policy without touching the adapter.
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

    /// <summary>
    /// Sprint-5 U6 — push a stock update to Shopee Open Platform v2's
    /// <c>POST /api/v2/product/update_stock</c> endpoint. The Polly v8
    /// retry pipeline injected by <see cref="ChannelServiceCollectionExtensions"/>
    /// wraps each attempt; <see cref="HttpRequestMessage"/> is rebuilt
    /// per attempt (single-shot send semantics).
    /// </summary>
    /// <remarks>
    /// Idempotency: the dispatcher (U5) provides a deterministic
    /// <see cref="StockUpdateRequest.IdempotencyKey"/> which we forward as
    /// <c>X-ShopFlow-Idempotency-Key</c>. The mock dedupes; real Shopee
    /// requires the upstream order/SKU keying — Phase-3 work.
    /// </remarks>
    public async Task<Result> PushStockUpdateAsync(
        StockUpdateRequest request,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = ShopeeStockUpdatePayload.From(request);

        try
        {
            using var response = await _retryPipeline
                .ExecuteAsync(
                    async tk =>
                    {
                        using var http = new HttpRequestMessage(
                            HttpMethod.Post,
                            "/api/v2/product/update_stock"
                        );
                        http.Headers.TryAddWithoutValidation(
                            "X-ShopFlow-Idempotency-Key",
                            request.IdempotencyKey
                        );
                        http.Content = JsonContent.Create(
                            payload,
                            options: ShopeeJson.Options
                        );
                        return await _httpClient.SendAsync(http, tk).ConfigureAwait(false);
                    },
                    ct
                )
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            var code = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "shopee.push.rate_limited",
                var s when (int)s >= 500 => "shopee.push.5xx",
                _ => "shopee.push.4xx",
            };
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Result.Failure(
                $"shopee push failed ({(int)response.StatusCode}): {body}",
                code
            );
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure(
                $"shopee push transport failure: {ex.Message}",
                "shopee.push.transport"
            );
        }
    }
}
