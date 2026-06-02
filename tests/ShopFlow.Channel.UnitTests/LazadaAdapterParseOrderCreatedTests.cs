using System.Text;
using Polly;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Infrastructure.Adapters;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Finish-line U7 — <see cref="LazadaAdapter.ParseOrderCreated"/> coverage.
/// Mirrors <see cref="ShopeeAdapterParseOrderCreatedTests"/> against the
/// Lazada order shape: <c>data.order_id</c>, <c>data.order_items[].sku</c>,
/// <c>data.order_items[].quantity</c>, <c>data.delivery_carrier</c>. Pins
/// the channel type, event-type gate, and stable failure codes.
/// </summary>
public sealed class LazadaAdapterParseOrderCreatedTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();

    private static LazadaAdapter NewAdapter()
    {
        var parser = new LazadaWebhookParser();
        var pipeline = ResiliencePipeline.Empty;
        var httpClient = new HttpClient();
        return new LazadaAdapter(parser, pipeline, httpClient);
    }

    private static WebhookEnvelope EnvelopeForOrderCreated(
        string rawPayload,
        string eventType = "order.created"
    ) =>
        new(
            ChannelId: ChannelId,
            ProviderEventId: "evt-1",
            EventType: eventType,
            RawPayload: rawPayload,
            OccurredAt: DateTime.UtcNow
        );

    private static string HappyPathPayload(int lineCount = 2, string? deliveryCarrier = "LEX") =>
        BuildPayload(
            orderId: "ORDER-LZ-001",
            items: Enumerable.Range(1, lineCount).Select(i => ($"LZ-SKU-{i:000}", i + 1)).ToArray(),
            deliveryCarrier: deliveryCarrier
        );

    private static string BuildPayload(
        string? orderId,
        (string Sku, int Qty)[]? items,
        string? deliveryCarrier = "LEX"
    )
    {
        var sb = new StringBuilder();
        sb.Append("{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", \"data\": { ");
        var dataParts = new List<string>();
        if (orderId is not null)
        {
            dataParts.Add($"\"order_id\": \"{orderId}\"");
        }
        if (items is not null)
        {
            var itemsJson = string.Join(
                ", ",
                items.Select(it => $"{{ \"sku\": \"{it.Sku}\", \"quantity\": {it.Qty} }}")
            );
            dataParts.Add($"\"order_items\": [{itemsJson}]");
        }
        if (deliveryCarrier is not null)
        {
            dataParts.Add($"\"delivery_carrier\": \"{deliveryCarrier}\"");
        }
        sb.Append(string.Join(", ", dataParts));
        sb.Append(" } }");
        return sb.ToString();
    }

    [Fact]
    public void ChannelType_IsLazada()
    {
        NewAdapter().ChannelType.Should().Be("lazada");
    }

    [Fact]
    public void HappyPath_TwoLines_ReturnsDraft()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(lineCount: 2));

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!;
        draft.ChannelExternalOrderId.Should().Be("ORDER-LZ-001");
        draft.ShippingProfile.Should().Be("LEX");
        draft.Lines.Should().HaveCount(2);
        draft.Lines[0].ExternalSku.Should().Be("LZ-SKU-001");
        draft.Lines[0].Qty.Should().Be(2);
        draft.Lines[1].ExternalSku.Should().Be("LZ-SKU-002");
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
        result.Value!.Lines[0].ExternalSku.Should().Be("LZ-SKU-001");
    }

    [Fact]
    public void MissingDeliveryCarrier_DefaultsToStandard()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(deliveryCarrier: null));

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ShippingProfile.Should().Be("standard");
    }

    [Fact]
    public void NonOrderCreatedEventType_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(HappyPathPayload(), eventType: "order.cancelled");

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.event_type_unsupported");
    }

    [Fact]
    public void MissingOrderId_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload = BuildPayload(orderId: null, items: new[] { ("LZ-1", 1) });
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.order_id_required");
    }

    [Fact]
    public void EmptyItems_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload = BuildPayload(orderId: "ORDER-1", items: Array.Empty<(string, int)>());
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.items_empty");
    }

    [Fact]
    public void MissingItems_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload = BuildPayload(orderId: "ORDER-1", items: null);
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.items_empty");
    }

    [Fact]
    public void LineMissingSku_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var payload =
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", "
            + "\"data\": { \"order_id\": \"ORDER-1\", \"order_items\": [ { \"sku\": \"OK-1\", \"quantity\": 1 }, "
            + "{ \"quantity\": 2 } ] } }";
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.line_sku_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void LineQuantityNonPositive_ReturnsFailure_WithStableCode(int badQty)
    {
        var adapter = NewAdapter();
        var payload =
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\", "
            + $"\"data\": {{ \"order_id\": \"ORDER-1\", \"order_items\": [ {{ \"sku\": \"LZ-1\", \"quantity\": {badQty} }} ] }} }}";
        var envelope = EnvelopeForOrderCreated(payload);

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.line_quantity_invalid");
    }

    [Fact]
    public void MalformedJsonInRawPayload_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(rawPayload: "{ not valid json");

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.data_malformed");
    }

    [Fact]
    public void DataMissingFromPayload_ReturnsFailure_WithStableCode()
    {
        var adapter = NewAdapter();
        var envelope = EnvelopeForOrderCreated(
            "{ \"event_id\": \"evt-1\", \"event_type\": \"order.created\" }"
        );

        var result = adapter.ParseOrderCreated(envelope);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.order.data_missing");
    }
}
