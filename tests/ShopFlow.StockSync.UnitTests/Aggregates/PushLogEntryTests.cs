using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.UnitTests.Aggregates;

public sealed class PushLogEntryTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Observed = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Pushed = new(2026, 5, 16, 12, 0, 1, DateTimeKind.Utc);

    [Fact]
    public void MarkSucceeded_SetsStatusAndNoErrorCode()
    {
        var entry = PushLogEntry.MarkSucceeded(
            tenantId: Tenant,
            channelType: "shopee",
            sku: "SKU-X",
            available: 7,
            idempotencyKey: "T:SKU-X:shopee:obs",
            latencyMs: 120,
            observedAt: Observed,
            pushedAt: Pushed
        );

        entry.Status.Should().Be("Success");
        entry.ErrorCode.Should().BeNull();
        entry.Available.Should().Be(7);
        entry.LatencyMs.Should().Be(120);
        entry.TenantId.Should().Be(Tenant);
        entry.ChannelType.Should().Be("shopee");
    }

    [Fact]
    public void MarkFailed_SetsErrorCodeAndStatus()
    {
        var entry = PushLogEntry.MarkFailed(
            tenantId: Tenant,
            channelType: "shopee",
            sku: "SKU-X",
            available: 7,
            idempotencyKey: "T:SKU-X:shopee:obs",
            errorCode: "shopee.push.5xx",
            latencyMs: 95,
            observedAt: Observed,
            pushedAt: Pushed
        );

        entry.Status.Should().Be("Failed");
        entry.ErrorCode.Should().Be("shopee.push.5xx");
        entry.LatencyMs.Should().Be(95);
    }

    [Fact]
    public void MarkBreakerOpen_SetsLatencyZeroAndStableErrorCode()
    {
        var entry = PushLogEntry.MarkBreakerOpen(
            tenantId: Tenant,
            channelType: "shopee",
            sku: "SKU-X",
            available: 7,
            idempotencyKey: "T:SKU-X:shopee:obs",
            observedAt: Observed,
            rejectedAt: Pushed
        );

        entry.Status.Should().Be("BreakerOpen");
        entry.ErrorCode.Should().Be("stocksync.breaker.open");
        entry.LatencyMs.Should().Be(0);
    }

    [Fact]
    public void MarkFailed_WithEmptyErrorCode_Throws()
    {
        Action act = () =>
            PushLogEntry.MarkFailed(
                tenantId: Tenant,
                channelType: "shopee",
                sku: "SKU-X",
                available: 7,
                idempotencyKey: "T:SKU-X:shopee:obs",
                errorCode: "",
                latencyMs: 0,
                observedAt: Observed,
                pushedAt: Pushed
            );

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("errorCode");
    }

    [Fact]
    public void MarkSucceeded_WithEmptyTenantId_Throws()
    {
        Action act = () =>
            PushLogEntry.MarkSucceeded(
                tenantId: Guid.Empty,
                channelType: "shopee",
                sku: "SKU-X",
                available: 7,
                idempotencyKey: "T:SKU-X:shopee:obs",
                latencyMs: 0,
                observedAt: Observed,
                pushedAt: Pushed
            );

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("tenantId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkSucceeded_WithBlankSku_Throws(string? sku)
    {
        Action act = () =>
            PushLogEntry.MarkSucceeded(
                tenantId: Tenant,
                channelType: "shopee",
                sku: sku!,
                available: 7,
                idempotencyKey: "T:X:shopee:obs",
                latencyMs: 0,
                observedAt: Observed,
                pushedAt: Pushed
            );

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("sku");
    }

    [Fact]
    public void MarkSucceeded_WithNegativeLatency_Throws()
    {
        Action act = () =>
            PushLogEntry.MarkSucceeded(
                tenantId: Tenant,
                channelType: "shopee",
                sku: "SKU-X",
                available: 7,
                idempotencyKey: "T:X:shopee:obs",
                latencyMs: -1,
                observedAt: Observed,
                pushedAt: Pushed
            );

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("latencyMs");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkSucceeded_WithBlankIdempotencyKey_Throws(string? key)
    {
        Action act = () =>
            PushLogEntry.MarkSucceeded(
                tenantId: Tenant,
                channelType: "shopee",
                sku: "SKU-X",
                available: 7,
                idempotencyKey: key!,
                latencyMs: 0,
                observedAt: Observed,
                pushedAt: Pushed
            );

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("idempotencyKey");
    }
}
