using System.Text;
using System.Text.Json;
using ShopFlow.Mocks.Lazada.Endpoints;
using ShopFlow.Mocks.Lazada.Signing;

// ─────────────────────────────────────────────────────────────────────────
// Lazada mock server — finish-line U7.
//
// The second marketplace mock alongside the Shopee mock (Sprint-4 U7).
// Emulates Lazada's webhook-source behaviour where it matters (HMAC-SHA256
// over raw body, base64 X-Lazada-Signature header, rate-limit headers,
// JSON envelope shape). Two control surfaces for tests:
//
//   POST /__send-webhook  — test driver constructs a Lazada order.created
//                            envelope, the mock signs it (X-Lazada-Signature)
//                            and POSTs it to Channel.Api.
//   POST /__chaos         — toggles 429 / 500 / latency injection rates.
//   POST /__seed-channel  — seeds a channel_id → secret binding so tests
//                            can drive their own channels without
//                            restarting the mock.
//
// The mock is dev/test only. Production Channel.Api connects to real Lazada
// endpoints; Channel:Lazada:MockBaseUrl is unset in prod config.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ChaosState>();
builder.Services.AddSingleton<SecretRegistry>();
builder.Services.AddHttpClient("receiver", c => c.Timeout = TimeSpan.FromSeconds(30));

var app = builder.Build();

// Seed initial channels from configuration (Aspire / appsettings injects them).
var seedRegistry = app.Services.GetRequiredService<SecretRegistry>();
foreach (var section in builder.Configuration.GetSection("Channels").GetChildren())
{
    if (
        Guid.TryParse(section["ChannelId"], out var channelId)
        && section["Secret"] is { Length: > 0 } secret
    )
    {
        seedRegistry.Register(channelId, Encoding.UTF8.GetBytes(secret));
    }
}

app.MapGet(
    "/",
    () =>
        Results.Ok(
            new
            {
                service = "shopflow-lazada-mock",
                purpose = "finish-line U7 — dev/test webhook source. NOT for production.",
            }
        )
);

app.MapPost(
    "/__chaos",
    (ChaosState chaos, ChaosUpdate body) =>
    {
        chaos.Rate429 = Math.Clamp(body.Rate429, 0d, 1d);
        chaos.Rate500 = Math.Clamp(body.Rate500, 0d, 1d);
        chaos.LatencyJitterMs = Math.Max(0, body.LatencyJitterMs);
        if (body.IsStockUpdateChaosActive.HasValue)
        {
            chaos.IsStockUpdateChaosActive = body.IsStockUpdateChaosActive.Value;
        }
        return Results.Ok(chaos);
    }
);

// Finish-line U7 — Lazada product v3 stock-update endpoint.
app.MapLazadaUpdateStock();

app.MapPost(
    "/__seed-channel",
    (SecretRegistry registry, SeedChannelRequest body) =>
    {
        registry.Register(body.ChannelId, Encoding.UTF8.GetBytes(body.SecretUtf8));
        return Results.Ok(new { seeded = body.ChannelId });
    }
);

app.MapPost(
    "/__send-webhook",
    async (
        SendWebhookRequest body,
        ChaosState chaos,
        SecretRegistry registry,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        CancellationToken ct
    ) =>
    {
        // Chaos injection — surface failure at the mock so the caller sees
        // a realistic transport-level failure mode.
        if (chaos.LatencyJitterMs > 0)
        {
            await Task.Delay(Random.Shared.Next(chaos.LatencyJitterMs), ct);
        }
        if (Random.Shared.NextDouble() < chaos.Rate500)
        {
            return Results.StatusCode(500);
        }
        if (Random.Shared.NextDouble() < chaos.Rate429)
        {
            return Results.Json(new { error = "rate limited" }, statusCode: 429);
        }

        var secret = registry.Get(body.ChannelId);
        if (secret is null)
        {
            return Results.NotFound(
                new { error = $"no secret seeded for channel {body.ChannelId}" }
            );
        }

        // Build a Lazada-shape order.created body. order_items default to a
        // single line so the receiver can parse a real order when the caller
        // doesn't specify items.
        var items = body.Items is { Length: > 0 }
            ? body.Items
            : new[] { new LazadaItem("LZ-DEFAULT", 1) };

        var envelope = new
        {
            event_id = body.EventId ?? Guid.NewGuid().ToString("N"),
            event_type = body.EventType,
            data = new
            {
                order_id = body.ExternalOrderId,
                order_items = items
                    .Select(it => new { sku = it.Sku, quantity = it.Quantity })
                    .ToArray(),
                delivery_carrier = body.DeliveryCarrier ?? "LEX",
            },
        };
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var signature = LazadaSigner.Sign(bodyBytes, secret);

        var receiverBase =
            body.ReceiverBaseUrl ?? config["Channel:ReceiverBaseUrl"] ?? "http://localhost:5181";
        var url =
            $"{receiverBase.TrimEnd('/')}/api/channel/webhooks/{body.ChannelType}/{body.ChannelId}";

        var client = httpFactory.CreateClient("receiver");
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.Add("Content-Type", "application/json");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("X-Lazada-Signature", signature);
        // Mock-side rate-limit advertising headers — the token bucket reads
        // these to size its budget.
        client.DefaultRequestHeaders.Add("X-Ratelimit-Limit", "1000");
        client.DefaultRequestHeaders.Add("X-Ratelimit-Remaining", "999");
        client.DefaultRequestHeaders.Add("X-Ratelimit-Reset", "60");

        using var response = await client.PostAsync(url, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return Results.Json(
            new
            {
                forwardedTo = url,
                receiverStatus = (int)response.StatusCode,
                receiverBody = responseBody,
                signaturePreview = signature[..Math.Min(signature.Length, 12)] + "…",
            }
        );
    }
);

await app.RunAsync();

// Finish-line U7 — IsStockUpdateChaosActive is nullable so callers that
// don't care about the flag keep their bodies unchanged; only senders that
// care set it.
internal sealed record ChaosUpdate(
    double Rate429,
    double Rate500,
    int LatencyJitterMs,
    bool? IsStockUpdateChaosActive = null
);

// Finish-line U7 — namespaced marker class for WebApplicationFactory<T> in
// the Channel integration suite. We intentionally avoid exposing a public
// top-level <c>Program</c> here because Channel.Api already ships one in the
// global namespace and global-Program collisions are undefined when the
// integration test project references both (mirrors the Shopee mock).
namespace ShopFlow.Mocks.Lazada
{
    public sealed class MockEntryPoint;
}
