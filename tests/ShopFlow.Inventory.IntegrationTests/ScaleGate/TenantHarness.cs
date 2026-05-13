using System.Diagnostics;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.IntegrationTests.ScaleGate;

/// <summary>
/// One scale-gate worker for one tenant — fires <c>callCount</c> concurrent
/// <see cref="ReservationRepository.TryReserveAsync"/> calls against the
/// tenant's DB and captures per-call latency for the fairness analysis.
/// </summary>
internal static class TenantHarness
{
    public static async Task<TenantRunResult> RunAsync(
        ProvisionedTenant tenant,
        string sku,
        int callCount,
        Quantity quantity,
        TimeSpan ttl,
        CancellationToken ct
    )
    {
        var orderIds = Enumerable
            .Range(0, callCount)
            .Select(i => $"SCALE-{tenant.Info.Slug}-{i:D5}")
            .ToArray();

        var latencies = new double[callCount];
        var outcomes = new ReserveOutcome[callCount];
        var errors = new string?[callCount];

        var sw = Stopwatch.StartNew();
        var tasks = new Task[callCount];
        for (var idx = 0; idx < callCount; idx++)
        {
            var i = idx;
            tasks[i] = Task.Run(
                async () =>
                {
                    var db = new InventoryDbContext(tenant.Options);
                    try
                    {
                        var repo = new ReservationRepository(
                            db,
                            TimeProvider.System,
                            tenant.BuildRequestContext()
                        );
                        var perCall = Stopwatch.StartNew();
                        var result = await repo.TryReserveAsync(
                                Sku.Create(sku),
                                orderIds[i],
                                quantity,
                                ttl,
                                ct
                            )
                            .ConfigureAwait(false);
                        perCall.Stop();
                        latencies[i] = perCall.Elapsed.TotalMilliseconds;
                        if (result.IsSuccess)
                        {
                            outcomes[i] = ReserveOutcome.Success;
                        }
                        else if (result.ErrorCode == "reservation.insufficient_stock")
                        {
                            outcomes[i] = ReserveOutcome.Oversold;
                        }
                        else
                        {
                            outcomes[i] = ReserveOutcome.OtherFailure;
                            errors[i] = result.ErrorCode ?? result.Error;
                        }
                    }
                    catch (Exception ex)
                    {
                        latencies[i] = -1;
                        outcomes[i] = ReserveOutcome.Exception;
                        errors[i] = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    finally
                    {
                        await db.DisposeAsync().ConfigureAwait(false);
                    }
                },
                ct
            );
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        sw.Stop();

        return new TenantRunResult(
            TenantSlug: tenant.Info.Slug,
            Latencies: latencies,
            Outcomes: outcomes,
            Errors: errors,
            TotalDuration: sw.Elapsed
        );
    }
}

internal enum ReserveOutcome
{
    Success = 0,
    Oversold = 1,
    OtherFailure = 2,
    Exception = 3,
}

internal sealed record TenantRunResult(
    string TenantSlug,
    double[] Latencies,
    ReserveOutcome[] Outcomes,
    string?[] Errors,
    TimeSpan TotalDuration
)
{
    public int SuccessCount => Outcomes.Count(o => o == ReserveOutcome.Success);

    public int OversoldCount => Outcomes.Count(o => o == ReserveOutcome.Oversold);

    public int OtherFailureCount =>
        Outcomes.Count(o => o == ReserveOutcome.OtherFailure || o == ReserveOutcome.Exception);

    public double SuccessLatencyP99
    {
        get
        {
            var successLatencies = Latencies
                .Where((_, i) => Outcomes[i] == ReserveOutcome.Success)
                .ToArray();
            return FairnessCalculator.Percentile(successLatencies, 99);
        }
    }

    /// <summary>
    /// Top-K most common error labels (code or exception type) across non-
    /// success / non-oversold outcomes. Use to diagnose what's really
    /// failing under contention.
    /// </summary>
    public IEnumerable<(string Label, int Count)> TopErrors(int k)
    {
        return Errors
            .Where(e => e is not null)
            .GroupBy(e => e!)
            .Select(g => (Label: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .Take(k);
    }
}
