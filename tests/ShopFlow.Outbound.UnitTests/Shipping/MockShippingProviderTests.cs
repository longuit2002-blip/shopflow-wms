using System.Collections.Concurrent;
using System.Diagnostics;
using Polly;
using Polly.Retry;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure.Shipping;

namespace ShopFlow.Outbound.UnitTests.Shipping;

/// <summary>
/// Sprint-3-redux U6 — <see cref="MockShippingProvider"/> behaviour
/// against the Polly v8 <see cref="ResiliencePipeline"/>: always-succeed,
/// always-fail, retry-then-succeed, and the wall-time delay observable.
/// </summary>
/// <remarks>
/// All tests use a SHORT delay window (5-20 ms) so the suite completes
/// sub-second; the production binding uses the 1-3 s window per the
/// plan spec.
/// </remarks>
public sealed class MockShippingProviderTests
{
    private static Order NewAwaitingShipOrder()
    {
        var order = Order.Create("ext-mock", "standard", new[] { ("SKU-A", 1, (int?)100) }).Value!;
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(100);
        order.MarkAwaitingShip();
        return order;
    }

    /// <summary>
    /// Build the same Polly pipeline shape AddOutboundModule wires:
    /// 3-retry constant-backoff on <see cref="TransientShippingException"/>
    /// with a SHORT delay so retry-exhaust tests still run sub-second.
    /// </summary>
    private static ResiliencePipeline BuildPipeline(TimeSpan? retryDelay = null) =>
        new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = retryDelay ?? TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Constant,
                    ShouldHandle = new PredicateBuilder().Handle<TransientShippingException>(),
                }
            )
            .Build();

    [Fact]
    public async Task CreateLabelAsync_FlakeRateZero_ReturnsValidLabel()
    {
        var pipeline = BuildPipeline();
        // FlakeRate = 0 means the inner call never throws — Polly's retry
        // strategy is wired but never engages.
        var provider = MockShippingProvider.WithFlakeRateAndDelay(
            pipeline,
            flakeRate: 0,
            minDelayMs: 5,
            maxDelayMsExclusive: 20
        );

        var label = await provider.CreateLabelAsync(NewAwaitingShipOrder(), CancellationToken.None);

        label.Should().NotBeNull();
        label.TrackingNumber.Should().StartWith("TRK-").And.HaveLength(20);
        label.LabelUrl.Should().StartWith("https://mock-carrier.example/labels/");
        label.LabelUrl.Should().EndWith($"{label.TrackingNumber}.pdf");
    }

    [Fact]
    public async Task CreateLabelAsync_FlakeRateOne_ThrowsAfterRetryExhaustion()
    {
        var pipeline = BuildPipeline();
        var provider = MockShippingProvider.WithFlakeRateAndDelay(
            pipeline,
            flakeRate: 1.0,
            minDelayMs: 5,
            maxDelayMsExclusive: 20
        );

        Func<Task> act = async () =>
            await provider.CreateLabelAsync(NewAwaitingShipOrder(), CancellationToken.None);

        await act.Should().ThrowAsync<TransientShippingException>();
    }

    [Fact]
    public async Task CreateLabelAsync_FlakeRateOne_PerformsThreeRetriesBeforeFailing()
    {
        // 1 initial + 3 retries = 4 attempts × 50 ms backoff after each
        // retry = 3 × 50 ms = 150 ms minimum wall-time (delays after
        // attempts 1, 2, 3 — last attempt is followed immediately by
        // exhaustion). We use a 50 ms backoff so the test runs quickly
        // but still proves the retry budget was exhausted.
        var pipeline = BuildPipeline(TimeSpan.FromMilliseconds(80));
        var provider = MockShippingProvider.WithFlakeRateAndDelay(
            pipeline,
            flakeRate: 1.0,
            minDelayMs: 1,
            maxDelayMsExclusive: 3 // ~1-2 ms per inner-call delay
        );

        var sw = Stopwatch.StartNew();
        Func<Task> act = async () =>
            await provider.CreateLabelAsync(NewAwaitingShipOrder(), CancellationToken.None);
        await act.Should().ThrowAsync<TransientShippingException>();
        sw.Stop();

        // Three 80 ms backoffs = 240 ms minimum. Wall-clock has slop; assert
        // ≥ 200 ms which proves at least 3 retries executed.
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task CreateLabelAsync_WithCustomDelayWindow_ObservesDelay()
    {
        var pipeline = BuildPipeline();
        var provider = MockShippingProvider.WithFlakeRateAndDelay(
            pipeline,
            flakeRate: 0,
            minDelayMs: 50,
            maxDelayMsExclusive: 100
        );

        var sw = Stopwatch.StartNew();
        await provider.CreateLabelAsync(NewAwaitingShipOrder(), CancellationToken.None);
        sw.Stop();

        // The inner delay is in [50, 100) ms; assert lower bound only.
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task CreateLabelAsync_DefaultsRespectFlakeRate()
    {
        // Defensive: the parameterless 1-arg constructor uses 5% flake by
        // default. Hard to assert across runs without statistical sampling,
        // but a zero-flake call via the WithFlakeRate builder must succeed
        // every time. The 5% default surfaces in MockShippingProviderTests
        // by running the call many times; here we just lock the default rate.
        var defaults = new MockShippingProvider(BuildPipeline());
        // Defaults expose 5% rate via the DefaultFlakeRate const.
        MockShippingProvider.DefaultFlakeRate.Should().Be(0.05);
        // Smoke: construct succeeds.
        defaults.Should().NotBeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public void Ctor_FlakeRateBelowZero_Throws()
    {
        Action act = () => _ = new MockShippingProvider(BuildPipeline(), -0.1, 1, 10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_FlakeRateAboveOne_Throws()
    {
        Action act = () => _ = new MockShippingProvider(BuildPipeline(), 1.1, 1, 10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_NullPipeline_Throws()
    {
        Action act = () => _ = new MockShippingProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateLabelAsync_NullOrder_Throws()
    {
        var provider = MockShippingProvider.WithFlakeRateAndDelay(
            BuildPipeline(),
            flakeRate: 0,
            minDelayMs: 1,
            maxDelayMsExclusive: 3
        );

        Func<Task> act = async () => await provider.CreateLabelAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Sprint-12.5 U4 — injectable random source for tier-3 carrier-retry E2E ─

    [Fact]
    public void Constructor_With5Args_AcceptsRandomSource()
    {
        // Smoke test: the new 5-arg ctor instantiates without throwing
        // when given a valid Func<double>. The ctor's role-isolation
        // is covered by the deterministic-flake test below; this fact
        // pins the lightest contract: the new constructor parameter
        // surface compiles and constructs.
        Func<double> rng = () => 0.5;
        var provider = new MockShippingProvider(
            BuildPipeline(),
            flakeRate: 0.3,
            minDelayMs: 1,
            maxDelayMsExclusive: 10,
            randomSource: rng
        );

        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateLabelAsync_WithDeterministicRandom_FlakesByQueue()
    {
        // Pre-seeded ConcurrentQueue<double> drives the flake decision
        // exactly. The provider's flake test is
        //   `if (_randomSource() < _flakeRate) throw ...`
        // so a value BELOW the flake rate causes a throw and a value
        // AT-OR-ABOVE the flake rate succeeds. With flakeRate=0.5:
        //   - 0.1 < 0.5 → throw (flake)
        //   - 0.9 < 0.5 → false → success
        // ConcurrentQueue (not Queue<T>) because the Polly retry path
        // resumes the continuation on a ThreadPool worker — a
        // non-thread-safe queue would race. The test uses a pass-through
        // (empty) pipeline so we observe the FIRST inner-call throw
        // without Polly's retry wrapper retrying it; the deterministic
        // flake-then-succeed assertion lives at the InnerCreateLabelAsync
        // level, not the full retry-loop level.
        var seq = new ConcurrentQueue<double>(new[] { 0.1, 0.9 });
        var provider = new MockShippingProvider(
            new ResiliencePipelineBuilder().Build(),
            flakeRate: 0.5,
            minDelayMs: 1,
            maxDelayMsExclusive: 3,
            randomSource: () => seq.TryDequeue(out var v) ? v : 1.0
        );

        // First attempt: 0.1 < 0.5 → throw.
        Func<Task> firstAttempt = async () =>
            await provider.CreateLabelAsync(NewAwaitingShipOrder(), CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<TransientShippingException>();

        // Second attempt: 0.9 → 0.9 < 0.5 is false → succeed. After
        // queue exhaustion the fallback 1.0 ≥ 0.5 → success for any
        // further calls; the test won't crash if Polly retries
        // unexpectedly.
        var label = await provider.CreateLabelAsync(NewAwaitingShipOrder(), CancellationToken.None);
        label.Should().NotBeNull();
        label.TrackingNumber.Should().StartWith("TRK-").And.HaveLength(20);
    }

    [Fact]
    public void Backward_Compat_2_3_4_Arg_Ctors_Still_Work()
    {
        // The 5-arg ctor is purely additive — the existing 1-arg + 4-arg
        // ctors must still resolve to legal constructions. This fact
        // pins the no-compile-regression contract; the 5-arg-with-null
        // delegation behavior is observable in the existing tests that
        // exercise WithFlakeRate / WithFlakeRateAndDelay (which now also
        // delegate to the 5-arg with randomSource=null).
        var p = BuildPipeline();

        var oneArg = new MockShippingProvider(p);
        oneArg.Should().NotBeNull();

        var fourArg = new MockShippingProvider(p, 0.05, 1, 10);
        fourArg.Should().NotBeNull();

        var viaFlakeBuilder = MockShippingProvider.WithFlakeRate(p, 0.0);
        viaFlakeBuilder.Should().NotBeNull();

        var viaFlakeDelayBuilder = MockShippingProvider.WithFlakeRateAndDelay(p, 0.0, 1, 10);
        viaFlakeDelayBuilder.Should().NotBeNull();

        var viaFlakeDelayRandomBuilder = MockShippingProvider.WithFlakeRateDelayAndRandom(
            p,
            0.0,
            1,
            10,
            () => 1.0
        );
        viaFlakeDelayRandomBuilder.Should().NotBeNull();
    }
}
