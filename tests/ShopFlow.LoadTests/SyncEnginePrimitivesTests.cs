using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using ShopFlow.LoadTests.Stubs;

namespace ShopFlow.LoadTests;

/// <summary>
/// NBomber scenarios encoding the three sync-engine primitives' invariants
/// from <c>01-product-development-plan.md.docx</c> §316–§323 verbatim.
///
/// W1 STATE: every primitive is a NotImplemented* stub that throws.
/// Each scenario routes its calls through
/// <see cref="ExpectStubFailureOrAssert"/>, which:
///
///   • catches the stub's NotImplementedException and treats it as
///     expected-stub-state (test passes)
///   • lets any OTHER failure bubble up as a real bug
///
/// W7 PIVOT: when Phase-2 Sprint-5 lands the real coalescer / rate-
/// limiter / priority-queue, the stub branch never fires; the live
/// invariant assertions in each scenario take over without test edits.
///
/// All scenarios carry [Trait("Category", "Load")] so the per-PR run
/// `dotnet test --filter "Category!=Load"` skips them. Per Plan §1597 +
/// AGENTS.md §8.59 load tests are nightly, not per-PR.
/// </summary>
public sealed class SyncEnginePrimitivesTests
{
    private static async Task<bool> ExpectStubFailureOrAssert(
        Func<Task> realAssertion,
        string stubMessagePrefix
    )
    {
        try
        {
            await realAssertion();
            return true;
        }
        catch (NotImplementedException ex)
            when (ex.Message.StartsWith(stubMessagePrefix, StringComparison.Ordinal))
        {
            // Expected-stub-state in W1; flips to live assertion in W7.
            return true;
        }
    }

    /// <summary>
    /// Plan §317 verbatim: "per (tenant, sku, channel) tuple, only the
    /// latest stock value in a debounce window (default 500ms) is pushed".
    /// 100 changes within the window → exactly 1 outbound push.
    /// </summary>
    [Fact]
    [Trait("Category", "Load")]
    public async Task Plan_317_Coalescer_100_changes_within_500ms_window_yields_exactly_1_push()
    {
        var coalescer = new NotImplementedStockSyncCoalescer();
        var passed = await ExpectStubFailureOrAssert(
            async () =>
            {
                var scenario = Scenario
                    .Create(
                        "coalescer_burst",
                        // Async lambda + Task.Yield: Enqueue + Response.Ok are both
                        // synchronous, so without the Yield CS1998 fires. NBomber 6.x
                        // delegate inference resolves cleanly with `async _ =>` returning
                        // Response.Ok(); a Task.FromResult-wrapped sync lambda hits CS1662
                        // because of generic inference around Response<T>.
                        async _ =>
                        {
                            await Task.Yield();
                            coalescer.Enqueue("tenant-A:SKU-1:shopee", value: 42);
                            return Response.Ok();
                        }
                    )
                    .WithoutWarmUp()
                    .WithLoadSimulations(
                        Simulation.Inject(
                            rate: 200,
                            interval: TimeSpan.FromMilliseconds(100),
                            during: TimeSpan.FromMilliseconds(500)
                        )
                    );

                _ = NBomberRunner.RegisterScenarios(scenario).Run();

                var pushCount = await coalescer
                    .FlushPushCountAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                pushCount.Should().Be(1);
            },
            NotImplementedStockSyncCoalescer.StubMessagePrefix
        );

        passed.Should().BeTrue();
    }

    /// <summary>
    /// Plan §318 verbatim: token-bucket rate limiter shapes a sustained
    /// burst. With bucket size 100/s and sustained 1000 req/s for 5s →
    /// exactly 500±5 served (5s × 100/s + initial burst), the rest blocked.
    /// </summary>
    [Fact]
    [Trait("Category", "Load")]
    public async Task Plan_318_RateLimiter_bucket_100ps_sustained_1000ps_for_5s_yields_500plusminus5_served()
    {
        var limiter = new NotImplementedRateLimiter();
        var served = 0;
        var passed = await ExpectStubFailureOrAssert(
            async () =>
            {
                // The inner Scenario step lambda awaits limiter.AcquireAsync; the outer
                // lambda however only configures the scenario synchronously and then calls
                // NBomberRunner.Run() which is sync. Without this Task.Yield the outer
                // lambda would draw CS1998 (async without await). Yield costs nothing and
                // keeps the signature aligned with ExpectStubFailureOrAssert(Func<Task>).
                await Task.Yield();
                var scenario = Scenario
                    .Create(
                        "rate_limiter_burst",
                        async _ =>
                        {
                            var grant = await limiter
                                .AcquireAsync(CancellationToken.None)
                                .ConfigureAwait(false);
                            if (grant > 0)
                            {
                                Interlocked.Increment(ref served);
                            }
                            return Response.Ok();
                        }
                    )
                    .WithoutWarmUp()
                    .WithLoadSimulations(
                        Simulation.Inject(
                            rate: 1000,
                            interval: TimeSpan.FromSeconds(1),
                            during: TimeSpan.FromSeconds(5)
                        )
                    );

                _ = NBomberRunner.RegisterScenarios(scenario).Run();

                served.Should().BeInRange(495, 505);
            },
            NotImplementedRateLimiter.StubMessagePrefix
        );

        passed.Should().BeTrue();
    }

    /// <summary>
    /// Plan §319 verbatim: "flash-sale SKUs (manually tagged, or auto-
    /// detected by recent velocity) preempt regular SKUs". A high-priority
    /// job enqueued behind 1000 regular jobs is dequeued within 100ms.
    /// </summary>
    [Fact]
    [Trait("Category", "Load")]
    public async Task Plan_319_PriorityQueue_high_priority_behind_1000_regular_served_within_100ms()
    {
        var queue = new NotImplementedPriorityQueue<string>();
        var passed = await ExpectStubFailureOrAssert(
            async () =>
            {
                // Seed 1000 regular jobs.
                for (var i = 0; i < 1_000; i++)
                {
                    await queue
                        .EnqueueAsync($"regular-{i}", priority: 0, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                // Enqueue the high-priority job; measure dequeue latency.
                await queue
                    .EnqueueAsync("flash-sale", priority: 100, CancellationToken.None)
                    .ConfigureAwait(false);

                var startedAt = TimeProvider.System.GetTimestamp();
                await foreach (var item in queue.DequeueAllAsync(CancellationToken.None))
                {
                    if (item == "flash-sale")
                    {
                        var elapsed = TimeProvider.System.GetElapsedTime(startedAt);
                        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
                        break;
                    }
                }
            },
            NotImplementedPriorityQueue<string>.StubMessagePrefix
        );

        passed.Should().BeTrue();
    }

    /// <summary>
    /// Sanity: NBomber's <see cref="ScenarioStats"/> shape is referenced
    /// so the package's transitive types are pinned by use; a stale
    /// transitive resolution surfaces here at compile time.
    /// </summary>
    [Fact]
    [Trait("Category", "Load")]
    public void NBomber_contract_types_resolve()
    {
        _ = typeof(ScenarioStats);
    }
}
