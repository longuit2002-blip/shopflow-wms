using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Polly;
using Polly.Retry;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Infrastructure.Adapters;
using Xunit;

namespace ShopFlow.Channel.UnitTests.Adapters;

/// <summary>
/// Sprint-5 plan U6 — <see cref="ShopeeAdapter.PushStockUpdateAsync"/>
/// coverage. Pins the wire shape (snake_case <c>item_id</c> +
/// <c>stock_list[0].normal_stock</c>), idempotency header forwarding, the
/// status-code → error-code mapping, and Polly retry interaction.
/// </summary>
public sealed class ShopeeAdapterPushStockUpdateTests
{
    private static StockUpdateRequest NewRequest(
        string externalSku = "123456",
        int quantity = 42,
        string idempotencyKey = "idem-1"
    ) =>
        new(
            ChannelId: Guid.Empty,
            ExternalSku: externalSku,
            Quantity: quantity,
            ObservedAt: new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc),
            IdempotencyKey: idempotencyKey
        );

    private static ShopeeAdapter NewAdapter(
        HttpMessageHandler handler,
        ResiliencePipeline? pipeline = null
    )
    {
        var parser = new ShopeeWebhookParser();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mock.local") };
        return new ShopeeAdapter(parser, pipeline ?? ResiliencePipeline.Empty, http);
    }

    private static ResiliencePipeline BuildRetryPipeline(int maxRetries) =>
        new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    Delay = TimeSpan.Zero,
                    BackoffType = DelayBackoffType.Constant,
                    // Polly v8 non-generic pipeline + generic PredicateBuilder<T>
                    // don't compose directly (RetryStrategyOptions.ShouldHandle is
                    // Func<RetryPredicateArguments<object>, ValueTask<bool>>). Hand-
                    // roll the predicate so the test stays on the non-generic
                    // pipeline contract the ShopeeAdapter ctor accepts. See
                    // docs/solutions/2026-05-20-polly-v8-predicatebuilder-non-generic.md.
                    ShouldHandle = args =>
                        args.Outcome switch
                        {
                            { Exception: HttpRequestException } => ValueTask.FromResult(true),
                            { Result: HttpResponseMessage r }
                                when (int)r.StatusCode >= 500
                                    || r.StatusCode == HttpStatusCode.TooManyRequests =>
                                ValueTask.FromResult(true),
                            _ => ValueTask.FromResult(false),
                        },
                }
            )
            .Build();

    [Fact]
    public async Task PushStockUpdateAsync_Returns_Success_When_Mock_Replies_200()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var adapter = NewAdapter(handler);

        // Act
        var result = await adapter.PushStockUpdateAsync(NewRequest(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v2/product/update_stock");
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
        result.ErrorCode.Should().Be("shopee.push.5xx");
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
        result.ErrorCode.Should().Be("shopee.push.rate_limited");
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
        result.ErrorCode.Should().Be("shopee.push.4xx");
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
            NewRequest(externalSku: "987654", quantity: 25),
            CancellationToken.None
        );

        var bodyText = handler.Bodies[0];
        bodyText.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(bodyText);
        doc.RootElement.GetProperty("item_id").GetInt64().Should().Be(987654L);
        var stockList = doc.RootElement.GetProperty("stock_list");
        stockList.GetArrayLength().Should().Be(1);
        stockList[0].GetProperty("model_id").GetInt64().Should().Be(0L);
        stockList[0].GetProperty("normal_stock").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task PushStockUpdateAsync_Non_Numeric_Sku_Falls_Back_To_Zero_Item_Id()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var adapter = NewAdapter(handler);

        await adapter.PushStockUpdateAsync(
            NewRequest(externalSku: "SP-SKU-001"),
            CancellationToken.None
        );

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        doc.RootElement.GetProperty("item_id").GetInt64().Should().Be(0L);
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
        // Each attempt should carry the idempotency header — the dispatcher's
        // dedup contract relies on the marketplace seeing the same key.
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
        result.ErrorCode.Should().Be("shopee.push.5xx");
        // 1 initial + 2 retries = 3 attempts total.
        handler.Requests.Should().HaveCount(3);
    }

    /// <summary>
    /// HttpMessageHandler stub that captures every outgoing request +
    /// body and replies via a per-call factory. <see cref="HttpRequestMessage"/>
    /// is single-shot send-only; the adapter rebuilds the message per
    /// retry attempt, so capturing inside SendAsync is safe.
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
