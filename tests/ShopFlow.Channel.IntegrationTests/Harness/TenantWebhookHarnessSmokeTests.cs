using System.Net;
using ShopFlow.Channel.IntegrationTests.Harness;

namespace ShopFlow.Channel.IntegrationTests;

/// <summary>
/// Sprint-4.5 plan U4 — smoke test confirming
/// <see cref="TenantWebhookHarness"/> is wired correctly end-to-end. Two
/// tenants, one webhook each, asserts per-tenant DB has exactly one
/// <c>webhook_events</c> row and zero cross-tenant contamination.
/// </summary>
/// <remarks>
/// Tagged <c>Category=Integration</c>; runs in CI's per-PR integration job
/// alongside <c>MigrationSmokeTests</c> + <c>CrossTenantRoutingTests</c>.
/// </remarks>
[Collection(ChannelWebhookCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TenantWebhookHarnessSmokeTests
{
    private readonly ChannelWebhookFixture _fixture;

    public TenantWebhookHarnessSmokeTests(ChannelWebhookFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TwoTenants_OneSignedWebhookEach_NoCrossTenantContamination()
    {
        await using var harness = new TenantWebhookHarness(_fixture);
        await harness.InitializeAsync(tenantCount: 2);

        // Seed mappings so the receiver doesn't fail on unmapped SKUs.
        await harness.SeedManualMappingAsync(tenantIndex: 0, "SP-T0-1", "INV-T0-1");
        await harness.SeedManualMappingAsync(tenantIndex: 1, "SP-T1-1", "INV-T1-1");

        var response0 = await harness.SendAsync(
            tenantIndex: 0,
            eventType: "order.created",
            ordersn: "ORDER-T0-001",
            items: new[] { ("SP-T0-1", 2) }
        );
        var response1 = await harness.SendAsync(
            tenantIndex: 1,
            eventType: "order.created",
            ordersn: "ORDER-T1-001",
            items: new[] { ("SP-T1-1", 3) }
        );

        response0.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        (await harness.CountWebhookEventsAsync(0)).Should().Be(1);
        (await harness.CountWebhookEventsAsync(1)).Should().Be(1);
        (await harness.CountOutboxRowsAsync(0)).Should().Be(1);
        (await harness.CountOutboxRowsAsync(1)).Should().Be(1);
    }
}
