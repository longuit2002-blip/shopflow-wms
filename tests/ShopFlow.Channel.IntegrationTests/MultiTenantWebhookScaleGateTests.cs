using System.Diagnostics;
using System.Net;
using ShopFlow.Channel.IntegrationTests.Harness;

namespace ShopFlow.Channel.IntegrationTests;

/// <summary>
/// Sprint-4 plan R10 + Sprint-4.5 U5 — multi-tenant webhook receiver
/// scale gate. The headline noisy-neighbor receiver-side assertions:
///
/// <list type="bullet">
///   <item><description>5 tenants × 200 webhooks/s × 5s sustained — p99 receiver-side latency &lt; 200ms per tenant.</description></item>
///   <item><description>Per-tenant fairness floor ≥ 0.85 — tenant A's burst does not push tenant B's p99 above its own SLO.</description></item>
///   <item><description>100× replay of the same <c>(channel_id, provider_event_id)</c> → exactly 1 <c>channel_outbox_messages</c> row.</description></item>
///   <item><description>Tenant A's secret signing a payload posted to Tenant B's <c>channel_id</c> → 401, zero rows in either tenant's DB.</description></item>
/// </list>
///
/// <para>Tagged <c>Category=Load</c>. Per-PR CI excludes; nightly +
/// on-demand CI runs them. Wall-time measurement on this dev machine
/// remains conditional on Docker availability per Sprint-1/3/4
/// precedent.</para>
/// </summary>
[Collection(ChannelWebhookCollection.Name)]
[Trait("Category", "Load")]
public sealed class MultiTenantWebhookScaleGateTests
{
    private readonly ChannelWebhookFixture _fixture;

    public MultiTenantWebhookScaleGateTests(ChannelWebhookFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Burst_5Tenants_200rps_5s_p99Under200ms()
    {
        const int tenantCount = 5;
        const int ratePerSecond = 200;
        const int durationSeconds = 5;
        const int webhooksPerTenant = ratePerSecond * durationSeconds;
        const double p99TargetMs = 200.0;
        const double fairnessFloorTarget = 0.85;

        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount);

        // Seed one mapping per tenant so happy-path lines resolve. Each
        // burst webhook reuses a single (external_sku, qty) pair — the
        // scale gate measures receiver-side throughput, not mapping
        // engine throughput.
        for (var i = 0; i < tenantCount; i++)
        {
            await harness.SeedManualMappingAsync(i, $"SP-T{i}", $"INV-T{i}");
        }

        // Warm-up — one synchronous webhook per tenant pre-burst so the
        // first measured request isn't paying first-time DbContext/EF
        // model-cache costs (matches Sprint-3-redux U8 pattern).
        for (var i = 0; i < tenantCount; i++)
        {
            using var warmup = await harness.SendAsync(
                tenantIndex: i,
                eventType: "order.created",
                ordersn: $"WARMUP-T{i}",
                items: new[] { ($"SP-T{i}", 1) }
            );
            warmup.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var latenciesByTenant = new Dictionary<int, List<double>>();
        for (var i = 0; i < tenantCount; i++)
        {
            latenciesByTenant[i] = new List<double>(webhooksPerTenant);
        }

        var burstTasks = new Task[tenantCount];
        for (var i = 0; i < tenantCount; i++)
        {
            var tenantIndex = i;
            burstTasks[i] = Task.Run(async () =>
            {
                for (var n = 0; n < webhooksPerTenant; n++)
                {
                    var sw = Stopwatch.StartNew();
                    using var response = await harness.SendAsync(
                        tenantIndex: tenantIndex,
                        eventType: "order.created",
                        ordersn: $"BURST-T{tenantIndex}-N{n:0000}",
                        items: new[] { ($"SP-T{tenantIndex}", 1) }
                    );
                    sw.Stop();
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    lock (latenciesByTenant)
                    {
                        latenciesByTenant[tenantIndex].Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
            });
        }

        await Task.WhenAll(burstTasks);

        var p99ByTenant = latenciesByTenant.ToDictionary(
            kv => kv.Key,
            kv => FairnessCalculator.Percentile(kv.Value, 99)
        );
        var fairness = FairnessCalculator.FairnessFloor(p99ByTenant);

        foreach (var (tenantIndex, p99) in p99ByTenant)
        {
            p99.Should()
                .BeLessThan(
                    p99TargetMs,
                    $"tenant {tenantIndex} receiver-side p99 must be < {p99TargetMs}ms under burst"
                );
        }
        fairness
            .Should()
            .BeGreaterThanOrEqualTo(
                fairnessFloorTarget,
                $"per-tenant fairness floor must be ≥ {fairnessFloorTarget} (min/max p99 across tenants)"
            );

        // Sanity — each tenant's DB has its full webhooks_per_tenant + 1 warmup
        // count after the burst.
        for (var i = 0; i < tenantCount; i++)
        {
            var rows = await harness.CountWebhookEventsAsync(i);
            rows.Should()
                .Be(
                    webhooksPerTenant + 1,
                    $"tenant {i} must have {webhooksPerTenant + 1} webhook_events rows (1 warmup + {webhooksPerTenant} burst)"
                );
        }
    }

    [Fact]
    public async Task Replay_SameProviderEventId_100Times_ExactlyOneOutboxRow()
    {
        const int replayCount = 100;
        const string fixedEventId = "evt-replay-fixed-12345";

        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 1);
        await harness.SeedManualMappingAsync(0, "SP-REPLAY", "INV-REPLAY");

        // Force the same Shopee envelope event_id across all 100 sends so
        // each call produces the same provider_event_id at parse time.
        // The receiver's UNIQUE constraint on
        // (channel_id, provider_event_id) catches replays in the
        // WebhookEventRepository.TryInsertAsync → 23505 → existing-row
        // return path. The orchestrator's outbox-append is gated on
        // IsDuplicate=false, so only the first call writes an outbox row.
        for (var n = 0; n < replayCount; n++)
        {
            using var response = await harness.SendAsync(
                tenantIndex: 0,
                eventType: "order.created",
                ordersn: "ORDER-REPLAY-001",
                items: new[] { ("SP-REPLAY", 1) },
                eventId: fixedEventId
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Exactly one webhook_events row + one channel_outbox_messages row —
        // 99 replays caught by UNIQUE-23505, no double outbox write.
        var webhookEventCount = await harness.CountWebhookEventsAsync(0);
        webhookEventCount
            .Should()
            .Be(1, "UNIQUE(channel_id, provider_event_id) must catch all 99 replays");
        var outboxCount = await harness.CountOutboxRowsAsync(0);
        outboxCount
            .Should()
            .Be(
                1,
                "exactly 1 OrderImportedV1 outbox row per (channel_id, provider_event_id) regardless of replay count"
            );
    }

    [Fact]
    public async Task CrossTenantSignature_Rejected_NoTenantDbRow()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 2);

        // Sign with tenant 0's secret, POST to tenant 1's channel URL.
        // Receiver verifies signature against tenant 1's stored secret →
        // mismatch → 401 BEFORE any DB writes (Sprint-4 controller order).
        using var response = await harness.SendAsync(
            tenantIndex: 1, // target URL = tenant 1's channel
            eventType: "order.created",
            ordersn: "ORDER-CROSS-TENANT",
            items: new[] { ("SP-CROSS", 1) },
            signWithTenantIndex: 0 // signed with tenant 0's secret
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Zero rows in EITHER tenant's DB — receiver bails before
        // touching the tenant DB.
        (await harness.CountWebhookEventsAsync(0))
            .Should()
            .Be(0);
        (await harness.CountWebhookEventsAsync(1)).Should().Be(0);
        (await harness.CountOutboxRowsAsync(0)).Should().Be(0);
        (await harness.CountOutboxRowsAsync(1)).Should().Be(0);
    }
}
