using System.Text;
using System.Text.Json;
using ShopFlow.Mocks.Shopee.Endpoints;
using ShopFlow.Mocks.Shopee.Signing;

// ─────────────────────────────────────────────────────────────────────────
// Shopee mock server — Sprint-4 plan U7.
//
// Emulates Shopee's webhook-source behaviour byte-for-byte where it
// matters (HMAC-SHA256 over raw body, base64 signature header, rate-limit
// headers, JSON envelope shape). Two control surfaces for tests:
//
//   POST /__send-webhook  — test driver constructs a webhook envelope,
//                            the mock signs it and POSTs it to Channel.Api.
//   POST /__chaos         — toggles 429 / 500 / latency injection rates.
//   POST /__seed-channel  — seeds a channel_id → secret binding so tests
//                            can drive their own channels without
//                            restarting the mock.
//
// The mock is dev/test only. Production Channel.Api connects to real
// Shopee endpoints; Channel:Shopee:MockBaseUrl is unset in prod config.
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
                service = "shopflow-shopee-mock",
                purpose = "Sprint-4 U7 — dev/test webhook source. NOT for production.",
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

// Sprint-5 U6 — Shopee Open Platform v2 stock-update endpoint.
app.MapShopeeUpdateStock();

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
            var headers = new Dictionary<string, string> { { "Retry-After", "1" } };
            return Results.Json(new { error = "rate limited" }, statusCode: 429);
        }

        var secret = registry.Get(body.ChannelId);
        if (secret is null)
        {
            return Results.NotFound(
                new { error = $"no secret seeded for channel {body.ChannelId}" }
            );
        }

        var envelope = new
        {
            event_id = body.EventId ?? Guid.NewGuid().ToString("N"),
            event_type = body.EventType,
            shop_id = 1L,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new { external_order_id = body.ExternalOrderId },
        };
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var signature = ShopeeSigner.Sign(bodyBytes, secret);

        var receiverBase =
            body.ReceiverBaseUrl ?? config["Channel:ReceiverBaseUrl"] ?? "http://localhost:5181";
        var url =
            $"{receiverBase.TrimEnd('/')}/api/channel/webhooks/{body.ChannelType}/{body.ChannelId}";

        var client = httpFactory.CreateClient("receiver");
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.Add("Content-Type", "application/json");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("X-Shopee-Signature", signature);
        // Mock-side rate-limit advertising headers — Sprint-5's token bucket
        // reads these to size its budget.
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

// Sprint-5 U6 — IsStockUpdateChaosActive added as nullable so existing
// callers (Sprint-4 tests) keep their bodies unchanged; only senders that
// care about the flag set it.
internal sealed record ChaosUpdate(
    double Rate429,
    double Rate500,
    int LatencyJitterMs,
    bool? IsStockUpdateChaosActive = null
);

// Sprint-5 U6 — namespaced marker class for WebApplicationFactory<T> in
// the Channel integration suite. We intentionally avoid exposing a
// public top-level <c>Program</c> here because Channel.Api already
// ships one in the global namespace and global-Program collisions are
// undefined when the integration test project references both.
namespace ShopFlow.Mocks.Shopee
{
    public sealed class MockEntryPoint;
}
