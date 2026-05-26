using MassTransit;
using ShopFlow.Contracts.Inventory;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.StockSync.IntegrationTests.Drivers;

/// <summary>
/// Sprint-5 plan U9 helper — emits <see cref="StockLevelChangedV1"/>
/// messages at a controllable per-tenant rate against an in-memory
/// MassTransit bus. The StockSync.Api boots with
/// <c>MessageBus:Transport=InMemory</c>; the
/// <see cref="MassTransit.IPublishEndpoint"/> resolved from the
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>'s
/// service provider routes published messages to the
/// <c>StockLevelChangedConsumer</c> registered in the
/// <c>StockSync.Application</c> assembly scan.
/// </summary>
/// <remarks>
/// <para>The plan's <em>research</em> default chose direct outbox-row
/// INSERT for scale-gate determinism. U9 here picks the IBus.Publish
/// path because the StockSync schema does NOT include an Inventory
/// outbox table — Inventory + StockSync share a single tenant DB in
/// production, but the StockSync.IntegrationTests fixture only applies
/// the StockSync migration. Booting Inventory's migration into the same
/// fixture would balloon the harness; the in-memory bus carries the
/// same payload + idempotency invariants with less moving parts. Each
/// publish is the moral equivalent of the multiplexed outbox having
/// dispatched the row to the broker — the path under test from
/// <c>StockLevelChangedConsumer</c> onwards is identical.</para>
///
/// <para>For high target rates (2k/s), the burst phase spawns
/// <c>parallelism</c> concurrent emit tasks, each emitting at
/// <c>rate / parallelism</c>. Per-task delay is computed as the inverse
/// of that sub-rate; for sub-rates above ~1k/s the delay falls below
/// 1ms and <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// becomes noisy — the driver tolerates this jitter because the
/// noisy-neighbor scale gate's hard assertion is fairness, not absolute
/// per-second throughput.</para>
///
/// <para>Each emission consumes one decrement from a per-driver
/// <c>available</c> counter starting at the configured initial value;
/// the consumer's coalescing buffer keeps only the latest
/// <c>(tenant, sku, channel)</c> entry so the absolute final value the
/// dispatcher pushes is the last decrement issued, not the cumulative
/// count.</para>
/// </remarks>
public sealed class TenantBurstDriver
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly TenantInfo _tenant;
    private readonly TimeProvider _clock;

    public TenantBurstDriver(
        IPublishEndpoint publishEndpoint,
        TenantInfo tenant,
        TimeProvider? clock = null
    )
    {
        ArgumentNullException.ThrowIfNull(publishEndpoint);
        ArgumentNullException.ThrowIfNull(tenant);
        _publishEndpoint = publishEndpoint;
        _tenant = tenant;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Emit one <see cref="StockLevelChangedV1"/> for
    /// <paramref name="sku"/> with the supplied
    /// <paramref name="available"/> stock count + an explicit
    /// <paramref name="observedAt"/> (defaults to clock-now).
    /// </summary>
    public Task EmitOneAsync(
        string sku,
        int available,
        DateTime? observedAt = null,
        CancellationToken ct = default
    )
    {
        var msg = new StockLevelChangedV1(
            TenantId: _tenant.Id,
            Sku: sku,
            AvailableToSell: available,
            OccurredAt: observedAt ?? _clock.GetUtcNow().UtcDateTime
        );
        return _publishEndpoint.Publish(msg, ct);
    }

    /// <summary>
    /// Burst <paramref name="ratePerSecond"/> events over
    /// <paramref name="duration"/> for one SKU. Spawns
    /// <paramref name="parallelism"/> concurrent emit tasks to absorb
    /// the high per-second target rates the scale gate exercises.
    /// Returns the total number of events emitted (best-effort under
    /// cancellation).
    /// </summary>
    public async Task<int> BurstAsync(
        string sku,
        int ratePerSecond,
        TimeSpan duration,
        int parallelism = 10,
        int initialAvailable = 1_000_000,
        CancellationToken ct = default
    )
    {
        if (ratePerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratePerSecond));
        }
        if (parallelism <= 0)
        {
            parallelism = 1;
        }

        var subRate = Math.Max(1, ratePerSecond / parallelism);
        var subDelayMs = Math.Max(0.0, 1000.0 / subRate);
        var endAt = _clock.GetUtcNow() + duration;
        var emitted = 0;

        var tasks = new Task[parallelism];
        for (var p = 0; p < parallelism; p++)
        {
            var pIndex = p;
            tasks[p] = Task.Run(
                async () =>
                {
                    var local = 0;
                    var available = initialAvailable - pIndex * 1000;
                    while (_clock.GetUtcNow() < endAt && !ct.IsCancellationRequested)
                    {
                        try
                        {
                            await EmitOneAsync(sku, available, ct: ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        available = Math.Max(0, available - 1);
                        local++;
                        Interlocked.Increment(ref emitted);
                        if (subDelayMs >= 1.0)
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(subDelayMs), ct)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }
                    }
                    _ = local; // suppress unused-warning under Release builds
                },
                ct
            );
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return emitted;
    }

    /// <summary>
    /// Drive a constant <paramref name="ratePerSecond"/> emit cadence for
    /// <paramref name="duration"/> on a single thread — used by the
    /// breaker-recovery test where the assertion is "tenant A's push
    /// throughput drops while tenant B stays steady", not absolute
    /// rate.
    /// </summary>
    public async Task<int> ConstantAsync(
        string sku,
        int ratePerSecond,
        TimeSpan duration,
        int initialAvailable = 10_000,
        CancellationToken ct = default
    )
    {
        var endAt = _clock.GetUtcNow() + duration;
        var delayMs = Math.Max(1, 1000 / Math.Max(1, ratePerSecond));
        var available = initialAvailable;
        var count = 0;
        while (_clock.GetUtcNow() < endAt && !ct.IsCancellationRequested)
        {
            try
            {
                await EmitOneAsync(sku, available, ct: ct).ConfigureAwait(false);
                available = Math.Max(0, available - 1);
                count++;
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
        return count;
    }
}
