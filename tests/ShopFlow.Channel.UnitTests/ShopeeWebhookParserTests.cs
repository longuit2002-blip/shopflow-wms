using System.Text;
using ShopFlow.Channel.Infrastructure.Adapters;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4 plan U5 — ShopeeWebhookParser coverage. Pins the
/// envelope-shape parsing + forward-compat handling of unknown fields.
/// </summary>
public sealed class ShopeeWebhookParserTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private readonly ShopeeWebhookParser _parser = new();

    private static ReadOnlySpan<byte> ToBytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Parse_HappyPath_ReturnsEnvelope()
    {
        var body =
            "{ \"event_id\": \"evt-abc-123\", \"event_type\": \"order.created\", "
            + "\"shop_id\": 42, \"timestamp\": 1730000000, \"data\": { \"x\": 1 } }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeTrue();
        var env = result.Value!;
        env.ChannelId.Should().Be(ChannelId);
        env.ProviderEventId.Should().Be("evt-abc-123");
        env.EventType.Should().Be("order.created");
        env.RawPayload.Should().Contain("\"x\": 1");
        env.OccurredAt.Should().Be(new DateTime(2024, 10, 27, 3, 33, 20, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_EmptyBody_Fails()
    {
        var result = _parser.Parse(ChannelId, ReadOnlySpan<byte>.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.body_empty");
    }

    [Fact]
    public void Parse_MalformedJson_Fails()
    {
        var result = _parser.Parse(ChannelId, ToBytes("{not json"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.body_malformed");
    }

    [Fact]
    public void Parse_MissingEventId_Fails()
    {
        var body = "{ \"event_type\": \"order.created\", \"shop_id\": 1, \"timestamp\": 1 }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.event_id_required");
    }

    [Fact]
    public void Parse_MissingEventType_Fails()
    {
        var body = "{ \"event_id\": \"e-1\", \"shop_id\": 1, \"timestamp\": 1 }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.event_type_required");
    }

    [Fact]
    public void Parse_UnknownFields_AreIgnored()
    {
        var body =
            "{ \"event_id\": \"e-1\", \"event_type\": \"order.created\", "
            + "\"shop_id\": 1, \"timestamp\": 1, \"future_field\": \"surprise\" }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderEventId.Should().Be("e-1");
    }

    [Fact]
    public void Parse_TrimsEventIdAndType()
    {
        var body =
            "{ \"event_id\": \"  e-1  \", \"event_type\": \"  order.created  \", "
            + "\"shop_id\": 1, \"timestamp\": 1 }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderEventId.Should().Be("e-1");
        result.Value!.EventType.Should().Be("order.created");
    }
}

/// <summary>
/// Sprint-4 plan U5 — ChannelAdapterFactory coverage.
/// </summary>
public sealed class ChannelAdapterFactoryTests
{
    private sealed class FakeAdapter : ShopFlow.Channel.Application.Adapters.IChannelAdapter
    {
        public FakeAdapter(string channelType)
        {
            ChannelType = channelType;
        }

        public string ChannelType { get; }

        public ShopFlow.SharedKernel.Domain.Result<ShopFlow.Channel.Application.Webhooks.WebhookEnvelope> ParseWebhook(
            Guid channelId,
            ReadOnlySpan<byte> body,
            IReadOnlyDictionary<string, string> headers
        ) => throw new NotSupportedException();

        public ShopFlow.SharedKernel.Domain.Result<ShopFlow.Channel.Application.Webhooks.ExternalOrderDraft> ParseOrderCreated(
            ShopFlow.Channel.Application.Webhooks.WebhookEnvelope envelope
        ) => throw new NotSupportedException();

        public Task<ShopFlow.SharedKernel.Domain.Result> PushStockUpdateAsync(
            ShopFlow.Channel.Application.Adapters.StockUpdateRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    [Fact]
    public void ResolveFor_KnownType_ReturnsAdapter()
    {
        var factory = new ChannelAdapterFactory(new[] { new FakeAdapter("shopee") });

        factory.ResolveFor("shopee").Should().NotBeNull();
        factory.ResolveFor("Shopee").Should().NotBeNull(); // case-insensitive
    }

    [Fact]
    public void ResolveFor_UnknownType_Throws()
    {
        var factory = new ChannelAdapterFactory(new[] { new FakeAdapter("shopee") });

        var act = () => factory.ResolveFor("lazada");

        act.Should()
            .Throw<ShopFlow.Channel.Application.Adapters.UnknownChannelTypeException>()
            .Which.ChannelType.Should()
            .Be("lazada");
    }

    [Fact]
    public void TryResolve_UnknownType_ReturnsNull()
    {
        var factory = new ChannelAdapterFactory(new[] { new FakeAdapter("shopee") });

        factory.TryResolve("lazada").Should().BeNull();
        factory.TryResolve("").Should().BeNull();
    }
}
