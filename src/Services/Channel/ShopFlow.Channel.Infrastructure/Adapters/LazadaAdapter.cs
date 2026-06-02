using System.Net;
using System.Net.Http.Json;
using Polly;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Infrastructure.Adapters.Lazada;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Infrastructure.Adapters;

/// <summary>
/// Lazada marketplace adapter (finish-line U7). Mirrors
/// <see cref="ShopeeAdapter"/> — stateless, parses inbound webhooks via
/// <see cref="LazadaWebhookParser"/> and pushes outbound stock updates
/// through a Polly-wrapped <see cref="HttpClient"/> against the Lazada
/// product API. The second marketplace channel proves the plugin
/// architecture: adding it touches <c>Adapters/Lazada/</c> + DI only, with
/// zero changes to <see cref="IChannelAdapter"/> or the factories.
/// </summary>
/// <remarks>
/// Outbound HTTP retry wrap (Polly v8 <see cref="ResiliencePipeline"/>) is
/// injected so the policy can be swapped without touching the adapter —
/// shares the same registered pipeline as the Shopee adapter.
/// </remarks>
public sealed class LazadaAdapter : IChannelAdapter
{
    private readonly LazadaWebhookParser _parser;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly HttpClient _httpClient;

    public LazadaAdapter(
        LazadaWebhookParser parser,
        ResiliencePipeline retryPipeline,
        HttpClient httpClient
    )
    {
        _parser = parser;
        _retryPipeline = retryPipeline;
        _httpClient = httpClient;
    }

    public string ChannelType => "lazada";

    public Result<WebhookEnvelope> ParseWebhook(
        Guid channelId,
        ReadOnlySpan<byte> body,
        IReadOnlyDictionary<string, string> headers
    )
    {
        // Headers reserved for future rate-limit-header round-trips into the
        // sync engine's per-channel token bucket; the receiver passes the
        // raw bytes plus channelId only at parse time.
        return _parser.Parse(channelId, body);
    }

    /// <summary>
    /// Finish-line U7 — extract Lazada's <c>data</c> field into the
    /// marketplace-agnostic <see cref="ExternalOrderDraft"/>. Delegates to
    /// <see cref="LazadaWebhookParser.ParseOrderData"/>; this method's job
    /// is event-type gating + Result wrapping.
    /// </summary>
    public Result<ExternalOrderDraft> ParseOrderCreated(WebhookEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.EventType, "order.created", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ExternalOrderDraft>.Failure(
                $"lazada.order: event_type '{envelope.EventType}' is not supported by ParseOrderCreated.",
                "lazada.order.event_type_unsupported"
            );
        }

        return _parser.ParseOrderData(envelope.RawPayload);
    }

    /// <summary>
    /// Finish-line U7 — push a stock update to the Lazada
    /// <c>POST /api/v3/product/update_stock</c> endpoint. The Polly v8
    /// retry pipeline injected by <see cref="ChannelServiceCollectionExtensions"/>
    /// wraps each attempt; <see cref="HttpRequestMessage"/> is rebuilt per
    /// attempt (single-shot send semantics).
    /// </summary>
    /// <remarks>
    /// Idempotency: the dispatcher provides a deterministic
    /// <see cref="StockUpdateRequest.IdempotencyKey"/> which we forward as
    /// <c>X-ShopFlow-Idempotency-Key</c> — mirrors the Shopee adapter's
    /// internal-audit header.
    /// </remarks>
    public async Task<Result> PushStockUpdateAsync(StockUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = LazadaStockUpdatePayload.From(request);

        try
        {
            using var response = await _retryPipeline
                .ExecuteAsync(
                    async tk =>
                    {
                        using var http = new HttpRequestMessage(
                            HttpMethod.Post,
                            "/api/v3/product/update_stock"
                        );
                        http.Headers.TryAddWithoutValidation(
                            "X-ShopFlow-Idempotency-Key",
                            request.IdempotencyKey
                        );
                        http.Content = JsonContent.Create(payload, options: LazadaJson.Options);
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
                HttpStatusCode.TooManyRequests => "lazada.push.rate_limited",
                var s when (int)s >= 500 => "lazada.push.5xx",
                _ => "lazada.push.4xx",
            };
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Result.Failure($"lazada push failed ({(int)response.StatusCode}): {body}", code);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure(
                $"lazada push transport failure: {ex.Message}",
                "lazada.push.transport"
            );
        }
    }
}
