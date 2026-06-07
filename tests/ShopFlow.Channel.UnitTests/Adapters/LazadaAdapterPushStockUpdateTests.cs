using System.Net;
using System.Text.Json;
using FluentAssertions;
using Polly;
using Polly.Retry;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Infrastructure.Adapters;
using Xunit;

namespace ShopFlow.Channel.UnitTests.Adapters;

/// <summary>
/// Finish-line U7 — <see cref="LazadaAdapter.PushStockUpdateAsync"/>
/// coverage. Mirrors <see cref="ShopeeAdapterPushStockUpdateTests"/>. Pins
/// the Lazada wire shape (snake_case <c>seller_sku</c> +
/// <c>sellable_stock[0].stock</c>), idempotency header forwarding, the
/// status-code → <c>lazada.push.*</c> error-code mapping, and Polly retry
/// interaction.
/// </summary>
public sealed class LazadaAdapterPushStockUpdateTests
{
    private static StockUpdateRequest NewRequest(
        string externalSku = "LZ-SKU-9",
        int quantity = 42,
        string idempotencyKey = "idem-1"
    ) =>
        new(
            ChannelId: Guid.Empty,
            ExternalSku: externalSku,
            Quantity: quantity,
            ObservedAt: new DateTime(2026, 5, 27, 10, 0, 0, DateTimeKind.Utc),
            IdempotencyKey: idempotencyKey
        );

    private static LazadaAdapter NewAdapter(
        HttpMessageHandler handler,
        ResiliencePipeline? pipeline = null
    )
    {
        var parser = new LazadaWebhookParser();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mock.local") };
        return new LazadaAdapter(parser, pipeline ?? ResiliencePipeline.Empty, http);
    }

    private static ResiliencePipeline BuildRetryPipeline(int maxRetries) =>
        new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    Delay = TimeSpan.Zero,
                    BackoffType = DelayBackoffType.Constant,
                    // Non-generic ResiliencePipeline → results flow as object.
                    // Hand-rolled predicate per Sprint-8.5 KTD2 (the installed
                    // Polly v8's non-generic PredicateBuilder.HandleResult takes
                    // Func<object,bool>, not a generic overload).
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .HandleResult(o =>
                            o is HttpResponseMessage r
                            && (
                                (int)r.StatusCode >= 500
                                || r.StatusCode == HttpStatusCode.TooManyRequests
                            )
                        ),
                }
            )
            .Build();

    [Fact]
    public async Task PushStockUpdateAsync_Returns_Success_When_Mock_Replies_200()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var adapter = NewAdapter(handler);

        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v3/product/update_stock");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task PushStockUpdateAsync_Maps_503_To_5xx_Code()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable
        )
        {
            Content = new StringContent("upstream unavailable"),
        });
        var adapter = NewAdapter(handler);

        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.push.5xx");
    }

    [Fact]
    public async Task PushStockUpdateAsync_Maps_429_To_Rate_Limited_Code()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(
            HttpStatusCode.TooManyRequests
        )
        {
            Content = new StringContent("rate limited"),
        });
        var adapter = NewAdapter(handler);

        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.push.rate_limited");
    }

    [Fact]
    public async Task PushStockUpdateAsync_Maps_400_To_4xx_Code()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request"),
        });
        var adapter = NewAdapter(handler);

        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.push.4xx");
    }

    [Fact]
    public async Task PushStockUpdateAsync_Forwards_Idempotency_Key_Header()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var adapter = NewAdapter(handler);

        await adapter.PushStockUpdateAsync(
            NewRequest(idempotencyKey: "idem-abc-123"),
            CancellationToken.None
        );

        handler
            .Requests[0]
            .Headers.GetValues("X-ShopFlow-Idempotency-Key")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("idem-abc-123");
    }

    [Fact]
    public async Task PushStockUpdateAsync_Serialises_Snake_Case_Payload_Shape()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var adapter = NewAdapter(handler);

        await adapter.PushStockUpdateAsync(
            NewRequest(externalSku: "LZ-987654", quantity: 25),
            CancellationToken.None
        );

        var bodyText = handler.Bodies[0];
        bodyText.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(bodyText);
        doc.RootElement.GetProperty("seller_sku").GetString().Should().Be("LZ-987654");
        var stockList = doc.RootElement.GetProperty("sellable_stock");
        stockList.GetArrayLength().Should().Be(1);
        stockList[0].GetProperty("warehouse_code").GetString().Should().Be("DEFAULT");
        stockList[0].GetProperty("stock").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task PushStockUpdateAsync_Retries_5xx_And_Surfaces_Success_On_Recovery()
    {
        var responses = new Queue<HttpResponseMessage>(
            new[]
            {
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("transient"),
                },
                new HttpResponseMessage(HttpStatusCode.OK),
            }
        );
        var handler = new RecordingHandler(_ => responses.Dequeue());
        var adapter = NewAdapter(handler, BuildRetryPipeline(maxRetries: 3));

        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        foreach (var req in handler.Requests)
        {
            req.Headers.GetValues("X-ShopFlow-Idempotency-Key")
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("idem-1");
        }
    }

    [Fact]
    public async Task PushStockUpdateAsync_Returns_Failure_When_Retries_Exhausted()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(
            HttpStatusCode.InternalServerError
        )
        {
            Content = new StringContent("still failing"),
        });
        var adapter = NewAdapter(handler, BuildRetryPipeline(maxRetries: 2));

        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("lazada.push.5xx");
        // 1 initial + 2 retries = 3 attempts total.
        handler.Requests.Should().HaveCount(3);
    }

    /// <summary>
    /// HttpMessageHandler stub that captures every outgoing request + body
    /// and replies via a per-call factory. Mirrors the Shopee adapter test's
    /// recording handler.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies.Add(
                    await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                );
            }
            else
            {
                Bodies.Add(string.Empty);
            }
            return _factory(request);
        }
    }
}
