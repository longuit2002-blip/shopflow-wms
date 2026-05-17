using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Infrastructure.Breaker;
using ShopFlow.StockSync.Infrastructure.Pipeline;

namespace ShopFlow.StockSync.UnitTests.Breaker;

/// <summary>
/// Sprint-5 plan U5 (R7) — <see cref="TenantChannelBreakerRegistry"/>
/// behavioural contract. Locks Closed → Open transition on threshold
/// failures, half-open probe after the break duration, and the
/// non-negotiable per-<c>(tenant, channel)</c> isolation that
/// underpins the Sprint-5 noisy-neighbor scale gate (R7 + KTD3).
/// </summary>
/// <remarks>
/// <para>Tests use a SHORT <see cref="StockSyncOptions.BreakerSettings.BreakDurationSeconds"/>
/// (1 s) so the half-open assertion completes inside the unit-test
/// budget without depending on <c>Microsoft.Extensions.TimeProvider.Testing</c>
/// (not on the CPM).</para>
///
/// <para>The Polly v8 sliding window with <c>FailureRatio = 1.0</c>
/// requires <em>every</em> action inside <c>SamplingDuration</c> to
/// fail, so tests run their failing executes back-to-back inside one
/// quick sweep — no successful executes mixed in.</para>
/// </remarks>
public sealed class TenantChannelBreakerRegistryTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static TenantChannelBreakerRegistry NewRegistry(
        int minimumThroughput = 5,
        int breakDurationSeconds = 1,
        int samplingDurationSeconds = 30
    )
    {
        var options = Options.Create(new StockSyncOptions
        {
            Breaker = new StockSyncOptions.BreakerSettings
            {
                MinimumThroughput = minimumThroughput,
                BreakDurationSeconds = breakDurationSeconds,
                SamplingDurationSeconds = samplingDurationSeconds,
            },
        });

        var factory = new PushPipelineFactory(
            options,
            NullLogger<PushPipelineFactory>.Instance
        );
        return new TenantChannelBreakerRegistry(factory);
    }

    [Fact]
    public async Task GetOrCreate_FreshPair_ExecutesSuccessfulCallAndReportsClosed()
    {
        var registry = NewRegistry();
        var pipeline = registry.GetOrCreate(TenantA, "shopee");

        var result = await pipeline
            .ExecuteAsync(static (_, _) => ValueTask.FromResult(Result.Success()), 0, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        registry.GetState(TenantA, "shopee").Should().Be("Closed");
    }

    [Fact]
    public async Task GetOrCreate_AfterThresholdFailures_BreakerTripsToOpen()
    {
        var registry = NewRegistry(
            minimumThroughput: 5,
            breakDurationSeconds: 60,
            samplingDurationSeconds: 30
        );
        var pipeline = registry.GetOrCreate(TenantA, "shopee");

        // 5 failing executes in a row inside one sampling window.
        for (var i = 0; i < 5; i++)
        {
            var failure = await pipeline.ExecuteAsync(
                static (_, _) => ValueTask.FromResult(Result.Failure("simulated", "test.fail")),
                0,
                CancellationToken.None
            );
            failure.IsSuccess.Should().BeFalse();
        }

        // 6th call must short-circuit with BrokenCircuitException.
        var act = async () => await pipeline.ExecuteAsync(
            static (_, _) =>
                ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );

        await act.Should().ThrowAsync<BrokenCircuitException>();
        registry.GetState(TenantA, "shopee").Should().Be("Open");
    }

    [Fact]
    public async Task GetOrCreate_AfterBreakDuration_ProbesHalfOpenThenClosesOnSuccess()
    {
        var registry = NewRegistry(
            minimumThroughput: 5,
            breakDurationSeconds: 1,
            samplingDurationSeconds: 30
        );
        var pipeline = registry.GetOrCreate(TenantA, "shopee");

        for (var i = 0; i < 5; i++)
        {
            _ = await pipeline.ExecuteAsync(
                static (_, _) => ValueTask.FromResult(Result.Failure("simulated", "test.fail")),
                0,
                CancellationToken.None
            );
        }
        registry.GetState(TenantA, "shopee").Should().Be("Open");

        // Wait past BreakDuration so the breaker is willing to admit
        // a probe. 1.2s buffer absorbs scheduler jitter on CI.
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        var probeResult = await pipeline.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );

        probeResult.IsSuccess.Should().BeTrue();
        registry.GetState(TenantA, "shopee").Should().Be("Closed");
    }

    [Fact]
    public async Task GetOrCreate_AfterBreakDuration_FailingProbeReturnsOpen()
    {
        var registry = NewRegistry(
            minimumThroughput: 5,
            breakDurationSeconds: 1,
            samplingDurationSeconds: 30
        );
        var pipeline = registry.GetOrCreate(TenantA, "shopee");

        for (var i = 0; i < 5; i++)
        {
            _ = await pipeline.ExecuteAsync(
                static (_, _) => ValueTask.FromResult(Result.Failure("simulated", "test.fail")),
                0,
                CancellationToken.None
            );
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        var probe = await pipeline.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Failure("still bad", "test.fail")),
            0,
            CancellationToken.None
        );

        probe.IsSuccess.Should().BeFalse();
        registry.GetState(TenantA, "shopee").Should().Be("Open");
    }

    [Fact]
    public async Task GetOrCreate_PerTenantChannel_IsolatesOpenState()
    {
        var registry = NewRegistry(
            minimumThroughput: 5,
            breakDurationSeconds: 60,
            samplingDurationSeconds: 30
        );

        var a_shopee = registry.GetOrCreate(TenantA, "shopee");
        var b_shopee = registry.GetOrCreate(TenantB, "shopee");
        var a_lazada = registry.GetOrCreate(TenantA, "lazada");

        // Trip only (TenantA, shopee).
        for (var i = 0; i < 5; i++)
        {
            _ = await a_shopee.ExecuteAsync(
                static (_, _) => ValueTask.FromResult(Result.Failure("simulated", "test.fail")),
                0,
                CancellationToken.None
            );
        }

        registry.GetState(TenantA, "shopee").Should().Be("Open");
        registry.GetState(TenantB, "shopee").Should().Be("Closed");
        registry.GetState(TenantA, "lazada").Should().Be("Closed");

        // Sibling pipelines must still execute normally.
        var b = await b_shopee.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );
        var al = await a_lazada.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );

        b.IsSuccess.Should().BeTrue();
        al.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void GetOrCreate_SamePair_ReturnsSameInstance()
    {
        var registry = NewRegistry();

        var first = registry.GetOrCreate(TenantA, "shopee");
        var second = registry.GetOrCreate(TenantA, "shopee");

        first.Should().BeSameAs(second);
    }
}
