using System.Net;
using ShopFlow.Channel.IntegrationTests.Harness;

namespace ShopFlow.Channel.IntegrationTests;

/// <summary>
/// Finish-line U7 / AE7 — Lazada webhook receive round-trip through the
/// real Channel.Api controller pipeline. Proves the K8 channel-agnostic
/// signature extraction verifies an <c>X-Lazada-Signature</c>-signed body,
/// the orchestrator parses the Lazada order shape, maps the lines, and
/// persists with <c>(channel_id, provider_event_id)</c> idempotency.
/// Replay of the same event_id collides on UNIQUE → exactly one webhook +
/// one outbox row.
/// </summary>
/// <remarks>
/// Tagged <c>Category=Integration</c>; needs a Docker-Postgres daemon
/// (Testcontainers) per the <see cref="ChannelWebhookFixture"/> shape.
/// </remarks>
[Collection(ChannelWebhookCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LazadaWebhookReceiveTests
{
    private readonly ChannelWebhookFixture _fixture;

    public LazadaWebhookReceiveTests(ChannelWebhookFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LazadaSignedOrderCreated_IsVerified_Parsed_AndPersisted()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 1, channelType: "lazada");

        await harness.SeedManualMappingAsync(tenantIndex: 0, "LZ-T0-1", "INV-T0-1");

        var response = await harness.SendLazadaAsync(
            tenantIndex: 0,
            eventType: "order.created",
            orderId: "ORDER-LZ-001",
            items: new[] { ("LZ-T0-1", 4) }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await harness.CountWebhookEventsAsync(0)).Should().Be(1);
        (await harness.CountOutboxRowsAsync(0)).Should().Be(1);

        // The emitted outbox row is an OrderImportedV1 with the resolved
        // internal SKU — proves the Lazada parse → map → assemble path.
        var outbox = await harness.GetOutboxRowsAsync(0);
        outbox.Should().ContainSingle();
        outbox[0].EventType.Should().Contain("OrderImportedV1");
        outbox[0].Payload.Should().Contain("INV-T0-1");
    }

    [Fact]
    public async Task LazadaReplay_SameEventId_PersistsExactlyOneRow()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 1, channelType: "lazada");
        await harness.SeedManualMappingAsync(tenantIndex: 0, "LZ-T0-1", "INV-T0-1");

        const string EventId = "evt-lazada-fixed-001";

        var first = await harness.SendLazadaAsync(
            tenantIndex: 0,
            eventType: "order.created",
            orderId: "ORDER-LZ-REPLAY",
            items: new[] { ("LZ-T0-1", 2) },
            eventId: EventId
        );
        var second = await harness.SendLazadaAsync(
            tenantIndex: 0,
            eventType: "order.created",
            orderId: "ORDER-LZ-REPLAY",
            items: new[] { ("LZ-T0-1", 2) },
            eventId: EventId
        );

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // (channel_id, provider_event_id) UNIQUE collapses the replay.
        (await harness.CountWebhookEventsAsync(0))
            .Should()
            .Be(1);
        (await harness.CountOutboxRowsAsync(0)).Should().Be(1);
    }

    [Fact]
    public async Task LazadaMissingSignatureHeader_Returns401_NoDbWrite()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 1, channelType: "lazada");
        await harness.SeedManualMappingAsync(tenantIndex: 0, "LZ-T0-1", "INV-T0-1");

        var response = await harness.SendLazadaAsync(
            tenantIndex: 0,
            eventType: "order.created",
            orderId: "ORDER-LZ-NOSIG",
            items: new[] { ("LZ-T0-1", 1) },
            omitSignatureHeader: true
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await harness.CountWebhookEventsAsync(0)).Should().Be(0);
    }

    [Fact]
    public async Task LazadaCrossTenantSignature_Returns401_NoDbWrite()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 2, channelType: "lazada");
        await harness.SeedManualMappingAsync(tenantIndex: 0, "LZ-T0-1", "INV-T0-1");

        // Sign tenant 0's webhook with tenant 1's secret — verification fails.
        var response = await harness.SendLazadaAsync(
            tenantIndex: 0,
            eventType: "order.created",
            orderId: "ORDER-LZ-XTENANT",
            items: new[] { ("LZ-T0-1", 1) },
            signWithTenantIndex: 1
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await harness.CountWebhookEventsAsync(0)).Should().Be(0);
    }
}

/// <summary>
/// Finish-line U7 K8 regression — the channel-agnostic signature extraction
/// refactor must NOT break the Shopee path. A valid <c>X-Shopee-Signature</c>
/// signed body still verifies + imports; a Shopee request with no signature
/// header now 401s the same way it did pre-K8.
/// </summary>
[Collection(ChannelWebhookCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ShopeeK8RegressionTests
{
    private readonly ChannelWebhookFixture _fixture;

    public ShopeeK8RegressionTests(ChannelWebhookFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ShopeeSignedOrderCreated_StillVerifies_AfterK8Refactor()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 1); // default channelType "shopee"
        await harness.SeedManualMappingAsync(tenantIndex: 0, "SP-T0-1", "INV-T0-1");

        var response = await harness.SendAsync(
            tenantIndex: 0,
            eventType: "order.created",
            ordersn: "ORDER-SP-K8",
            items: new[] { ("SP-T0-1", 3) }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await harness.CountWebhookEventsAsync(0)).Should().Be(1);
        (await harness.CountOutboxRowsAsync(0)).Should().Be(1);
    }
}
