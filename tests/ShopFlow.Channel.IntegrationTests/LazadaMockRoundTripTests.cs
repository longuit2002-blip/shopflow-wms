using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Polly;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Infrastructure.Adapters;
using ShopFlow.Mocks.Lazada;
using Xunit;

namespace ShopFlow.Channel.IntegrationTests;

/// <summary>
/// Finish-line U7 — end-to-end round trip: <see cref="LazadaAdapter"/>
/// sends a real HTTP POST to the in-process Lazada mock booted via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>. Mirrors
/// <see cref="ShopeeMockRoundTripTests"/> — asserts the happy 200 path +
/// chaos-toggled 503 path. No Postgres / no Testcontainers; exercises the
/// HTTP boundary only.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LazadaMockRoundTripTests : IClassFixture<WebApplicationFactory<MockEntryPoint>>
{
    private readonly WebApplicationFactory<MockEntryPoint> _factory;

    public LazadaMockRoundTripTests(WebApplicationFactory<MockEntryPoint> factory)
    {
        _factory = factory;
    }

    private LazadaAdapter NewAdapterAgainstMock()
    {
        var http = _factory.CreateClient();
        return new LazadaAdapter(new LazadaWebhookParser(), ResiliencePipeline.Empty, http);
    }

    private static StockUpdateRequest NewRequest(string sku = "LZ-SELLER-9", int qty = 17) =>
        new(
            ChannelId: Guid.Empty,
            ExternalSku: sku,
            Quantity: qty,
            ObservedAt: DateTime.UtcNow,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

    [Fact]
    public async Task PushStockUpdateAsync_RoundTrips_Through_Mock_With_200_Ok()
    {
        await ResetChaosAsync();

        var adapter = NewAdapterAgainstMock();
        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task PushStockUpdateAsync_Returns_5xx_When_Mock_Chaos_Toggled()
    {
        await SetStockUpdateChaosAsync(active: true);
        try
        {
            var adapter = NewAdapterAgainstMock();
            var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("lazada.push.5xx");
        }
        finally
        {
            await ResetChaosAsync();
        }
    }

    private async Task ResetChaosAsync()
    {
        using var http = _factory.CreateClient();
        var response = await http.PostAsJsonAsync(
            "/__chaos",
            new
            {
                Rate429 = 0d,
                Rate500 = 0d,
                LatencyJitterMs = 0,
                IsStockUpdateChaosActive = false,
            }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task SetStockUpdateChaosAsync(bool active)
    {
        using var http = _factory.CreateClient();
        var response = await http.PostAsJsonAsync(
            "/__chaos",
            new
            {
                Rate429 = 0d,
                Rate500 = 0d,
                LatencyJitterMs = 0,
                IsStockUpdateChaosActive = active,
            }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
