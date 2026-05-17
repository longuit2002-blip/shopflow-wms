using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Infrastructure.Pipeline;

namespace ShopFlow.StockSync.UnitTests.Pipeline;

/// <summary>
/// Sprint-5 plan U5 — direct contract tests for
/// <see cref="PushPipelineFactory"/>. The factory's job is to translate
/// <see cref="StockSyncOptions.BreakerSettings"/> into a Polly v8
/// pipeline whose trip predicate handles both result-shape failures
/// (adapter returned <see cref="Result"/> with <c>IsSuccess=false</c>)
/// and thrown exceptions. These tests cover the two predicate paths
/// independently of the registry.
/// </summary>
public sealed class PushPipelineTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PushPipelineFactory NewFactory(
        int minimumThroughput = 5,
        int breakDurationSeconds = 60
    )
    {
        return new PushPipelineFactory(
            Options.Create(new StockSyncOptions
            {
                Breaker = new StockSyncOptions.BreakerSettings
                {
                    MinimumThroughput = minimumThroughput,
                    BreakDurationSeconds = breakDurationSeconds,
                    SamplingDurationSeconds = 30,
                },
            }),
            NullLogger<PushPipelineFactory>.Instance
        );
    }

    [Fact]
    public async Task Build_PipelinePassesThroughSuccessResultsUnchanged()
    {
        var bundle = NewFactory().Build(TenantId, "shopee");

        var result = await bundle.Pipeline.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        bundle.StateProvider.CircuitState.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Build_PipelineCountsResultFailuresTowardTrip()
    {
        var bundle = NewFactory(minimumThroughput: 3).Build(TenantId, "shopee");

        for (var i = 0; i < 3; i++)
        {
            _ = await bundle.Pipeline.ExecuteAsync(
                static (_, _) => ValueTask.FromResult(Result.Failure("simulated", "code")),
                0,
                CancellationToken.None
            );
        }

        var act = async () => await bundle.Pipeline.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );

        await act.Should().ThrowAsync<BrokenCircuitException>();
        bundle.StateProvider.CircuitState.Should().Be(CircuitState.Open);
    }

    private static ValueTask<Result> ThrowingCallback(int _, CancellationToken __)
    {
        throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Build_PipelineCountsExceptionsTowardTrip()
    {
        var bundle = NewFactory(minimumThroughput: 3).Build(TenantId, "shopee");

        for (var i = 0; i < 3; i++)
        {
            var act = async () => await bundle.Pipeline.ExecuteAsync(
                ThrowingCallback,
                0,
                CancellationToken.None
            );
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // After 3 thrown exceptions the breaker is open: any next
        // execute short-circuits.
        var next = async () => await bundle.Pipeline.ExecuteAsync(
            static (_, _) => ValueTask.FromResult(Result.Success()),
            0,
            CancellationToken.None
        );
        await next.Should().ThrowAsync<BrokenCircuitException>();
        bundle.StateProvider.CircuitState.Should().Be(CircuitState.Open);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_RejectsNonPositiveMinimumThroughput(int value)
    {
        var act = () => new PushPipelineFactory(
            Options.Create(new StockSyncOptions
            {
                Breaker = new StockSyncOptions.BreakerSettings
                {
                    MinimumThroughput = value,
                },
            }),
            NullLogger<PushPipelineFactory>.Instance
        );
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
