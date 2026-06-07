using System.Text;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Infrastructure.Adapters;
using ShopFlow.Channel.Infrastructure.Signature;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Finish-line U7 — LazadaWebhookParser envelope coverage. Mirrors
/// <see cref="ShopeeWebhookParserTests"/>: pins the envelope-shape parsing
/// + forward-compat handling of unknown fields against the Lazada
/// <c>{event_id, event_type, data}</c> wrapper.
/// </summary>
public sealed class LazadaWebhookParserTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private readonly LazadaWebhookParser _parser = new();

    private static ReadOnlySpan<byte> ToBytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Parse_HappyPath_ReturnsEnvelope()
    {
        var body =
            "{ \"event_id\": \"evt-abc-123\", \"event_type\": \"order.created\", "
            + "\"data\": { \"x\": 1 } }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeTrue();
        var env = result.Value!;
        env.ChannelId.Should().Be(ChannelId);
        env.ProviderEventId.Should().Be("evt-abc-123");
        env.EventType.Should().Be("order.created");
        env.RawPayload.Should().Contain("\"x\": 1");
    }

    [Fact]
    public void Parse_EmptyBody_Fails()
    {
        var result = _parser.Parse(ChannelId, ReadOnlySpan<byte>.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.body_empty");
    }

    [Fact]
    public void Parse_MalformedJson_Fails()
    {
        var result = _parser.Parse(ChannelId, ToBytes("{not json"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.body_malformed");
    }

    [Fact]
    public void Parse_MissingEventId_Fails()
    {
        var body = "{ \"event_type\": \"order.created\", \"data\": { } }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.event_id_required");
    }

    [Fact]
    public void Parse_MissingEventType_Fails()
    {
        var body = "{ \"event_id\": \"e-1\", \"data\": { } }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.event_type_required");
    }

    [Fact]
    public void Parse_UnknownFields_AreIgnored()
    {
        var body =
            "{ \"event_id\": \"e-1\", \"event_type\": \"order.created\", "
            + "\"future_field\": \"surprise\", \"data\": { } }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderEventId.Should().Be("e-1");
    }

    [Fact]
    public void Parse_TrimsEventIdAndType()
    {
        var body =
            "{ \"event_id\": \"  e-1  \", \"event_type\": \"  order.created  \", \"data\": { } }";

        var result = _parser.Parse(ChannelId, ToBytes(body));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderEventId.Should().Be("e-1");
        result.Value!.EventType.Should().Be("order.created");
    }
}

/// <summary>
/// Finish-line U7 — plugin-architecture proof. The factories DI-enumerate
/// every registered adapter / verifier and index by channel type; adding
/// Lazada means both factories resolve it with no factory-shape change.
/// </summary>
public sealed class MultiChannelFactoryResolutionTests
{
    [Fact]
    public void SignatureVerifierFactory_Resolves_Both_Shopee_And_Lazada()
    {
        var factory = new SignatureVerifierFactory(
            new ISignatureVerifier[]
            {
                new ShopeeSignatureVerifier(),
                new LazadaSignatureVerifier(),
            }
        );

        factory.Resolve("shopee").Should().NotBeNull();
        factory.Resolve("lazada").Should().NotBeNull();
        factory.Resolve("Lazada").Should().NotBeNull(); // case-insensitive
        factory.Resolve("tiktok").Should().BeNull();
    }

    [Fact]
    public void Resolved_Verifiers_Expose_DistinctSignatureHeaderNames()
    {
        var factory = new SignatureVerifierFactory(
            new ISignatureVerifier[]
            {
                new ShopeeSignatureVerifier(),
                new LazadaSignatureVerifier(),
            }
        );

        factory.Resolve("shopee")!.SignatureHeaderName.Should().Be("X-Shopee-Signature");
        factory.Resolve("lazada")!.SignatureHeaderName.Should().Be("X-Lazada-Signature");
    }

    [Fact]
    public void ChannelAdapterFactory_Resolves_Both_Shopee_And_Lazada()
    {
        var shopee = new ShopeeAdapter(
            new ShopeeWebhookParser(),
            Polly.ResiliencePipeline.Empty,
            new HttpClient()
        );
        var lazada = new LazadaAdapter(
            new LazadaWebhookParser(),
            Polly.ResiliencePipeline.Empty,
            new HttpClient()
        );
        var factory = new ChannelAdapterFactory(
            new ShopFlow.Channel.Application.Adapters.IChannelAdapter[] { shopee, lazada }
        );

        factory.ResolveFor("shopee").ChannelType.Should().Be("shopee");
        factory.ResolveFor("lazada").ChannelType.Should().Be("lazada");
        factory.TryResolve("tiktok").Should().BeNull();
    }
}
