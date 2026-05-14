using System.Text;
using Polly;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Infrastructure.Adapters;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4.5 plan U1 — <see cref="ShopeeAdapter.ParseOrderCreated"/>
/// coverage. Pins the Shopee Open Platform v2 order-shape extraction:
/// <c>data.ordersn</c>, <c>data.items[].item_sku</c>,
/// <c>data.items[].model_quantity_purchased</c>,
/// <c>data.package_list[0].shipping_carrier</c>. Field names follow
/// <c>tests/fixtures/channels/shopee/webhook-order-created.json</c>.
/// </summary>
public sealed class ShopeeAdapterParseOrderCreatedTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();

    private static ShopeeAdapter NewAdapter()
    {
        var parser = new ShopeeWebhookParser();
        var pipeline = ResiliencePipeline.Empty;
        var httpClient = new HttpClient();
        return new ShopeeAdapter(parser, pipeline, httpClient);
    }

    private static WebhookEnvelope EnvelopeForOrderCreated(string rawPayload, string eventType = "order.created") =>
        new(
            ChannelId: ChannelId,
            ProviderEventId: "evt-1",
            EventType: eventType,
            RawPayload: rawPayload,
            OccurredAt: DateTime.UtcNow
        );

    private static string HappyPathPayload(int lineCount = 2, string? shippingCarrier = "GHN") =>
        BuildPayload(
            ordersn: "ORDER-SP-001",
            items: Enumerable
                .Range(1, lineCount)
                .Select(i => ($"SP-SKU-{i:000}", i + 1))
                .ToArray(),
            shippingCarrier: shippingCarrier
        );

    private static string BuildPayload(
        string? ordersn,
        (string Sku, int Qty)[]? items,
        string? shippingCarrier = "GHN"
    )
    {
        var sb = new StringBuilder();
        sb.Append("{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", \"shop_id\": 42, \"timestamp\": 1730000000, \"data\": { ");
        var dataParts = new List<string>();
        if (ordersn is not null)
        {
            dataParts.Add($"\"ordersn\": \"{ordersn}\"");
        }
        if (items is not null)
        {
            var itemsJson = string.Join(
                ", ",
                items.Select(it =>
                    $"{{ \"item_sku\": \"{it.Sku}\", \"model_quantity_purchased\": {it.Qty} }}"
                )
            );
            dataParts.Add($"\"items\": [{itemsJson}]");
        }
        if (shippingCarrier is not null)
        {
            dataParts.Add(
                $"\"package_list\": [{{ \"shipping_carrier\": \"{shippingCarrier}\" }}]"
            );
        }
        sb.Append(string.Join(", ", dataParts));
        sb.Append(" } }");
        return sb.ToString();
    }

    [Fact]
    public void HappyPath_TwoLines_ReturnsDraft()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(lineCount: 2));

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!;
        draft.ChannelExternalOrderId.Should().Be("ORDER-SP-001");
        draft.ShippingProfile.Should().Be("GHN");
        draft.Lines.Should().HaveCount(2);
        draft.Lines[0].ExternalSku.Should().Be("SP-SKU-001");
        draft.Lines[0].Qty.Should().Be(2);
        draft.Lines[1].ExternalSku.Should().Be("SP-SKU-002");
        draft.Lines[1].Qty.Should().Be(3);
    }

    [Fact]
    public void HappyPath_SingleLine_ReturnsDraftWithOneLine()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(lineCount: 1));

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Lines.Should().ContainSingle();
        result.Value!.Lines[0].ExternalSku.Should().Be("SP-SKU-001");
    }

    [Fact]
    public void MissingShippingCarrier_DefaultsTo_DefaultProfile()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(shippingCarrier: null));

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ShippingProfile.Should().Be("default");
    }

    [Fact]
    public void NonOrderCreatedEventType_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(), eventType: "order.cancelled");

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.event_type_unsupported");
    }

    [Fact]
    public void MissingOrdersn_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload = BuildPayload(ordersn: null, items: new[] { ("SP-1", 1) });
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.ordersn_required");
    }

    [Fact]
    public void EmptyItems_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload = BuildPayload(ordersn: "ORDER-1", items: Array.Empty<(string, int)>());
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.items_empty");
    }

    [Fact]
    public void MissingItems_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload = BuildPayload(ordersn: "ORDER-1", items: null);
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.items_empty");
    }

    [Fact]
    public void LineMissingItemSku_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        // Hand-build payload with one good line and one missing item_sku.
        var payload =
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", \"shop_id\": 42, \"timestamp\": 1730000000, "
            + "\"data\": { \"ordersn\": \"ORDER-1\", \"items\": [ { \"item_sku\": \"OK-1\", \"model_quantity_purchased\": 1 }, "
            + "{ \"model_quantity_purchased\": 2 } ] } }";
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.line_sku_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void LineQuantityNonPositive_ReturnsFailure_WithStableCode(int badQty)
    {
        var adapter = NewAdapter();
        var payload =
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", \"shop_id\": 42, \"timestamp\": 1730000000, "
            + $"\"data\": {{ \"ordersn\": \"ORDER-1\", \"items\": [ {{ \"item_sku\": \"SP-1\", \"model_quantity_purchased\": {badQty} }} ] }} }}";
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.line_quantity_invalid");
    }

    [Fact]
    public void MalformedJsonInRawPayload_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(rawPayload: "{ not valid json");

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.data_malformed");
    }

    [Fact]
    public void DataMissingFromPayload_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", \"shop_id\": 42, \"timestamp\": 1730000000 }"
        );

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.data_missing");
    }

    [Fact]
    public void HappyPath_RealShopeeFixtureShape_ReturnsDraft()
    {
        // Mirrors tests/fixtures/channels/shopee/webhook-order-created.json
        // shape — one item with model_quantity_purchased=2, shipping_carrier="EXAMPLE_CARRIER".
        var adapter = NewAdapter();
        var payload =
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", \"shop_id\": 9999000111, \"timestamp\": 1769472000, "
            + "\"data\": { \"ordersn\": \"EXAMPLE_2604ABCDEFGH\", \"items\": [ { \"item_sku\": \"EXAMPLE-SKU-MOUSE-BLK-A\", \"model_quantity_purchased\": 2 } ], "
            + "\"package_list\": [ { \"shipping_carrier\": \"EXAMPLE_CARRIER\" } ] } }";
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!;
        draft.ChannelExternalOrderId.Should().Be("EXAMPLE_2604ABCDEFGH");
        draft.ShippingProfile.Should().Be("EXAMPLE_CARRIER");
        draft.Lines.Should().ContainSingle();
        draft.Lines[0].ExternalSku.Should().Be("EXAMPLE-SKU-MOUSE-BLK-A");
        draft.Lines[0].Qty.Should().Be(2);
    }
}
