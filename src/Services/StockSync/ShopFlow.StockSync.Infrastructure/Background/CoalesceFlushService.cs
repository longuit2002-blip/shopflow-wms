using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Options;

namespace ShopFlow.StockSync.Infrastructure.Background;

/// <summary>
/// <see cref="BackgroundService"/> that ticks on a
/// <see cref="PeriodicTimer"/> set to
/// <see cref="StockSyncOptions.CoalesceWindowMs"/> (default 500ms) and,
/// on each tick, drains the singleton <see cref="ICoalescingBuffer"/>
/// via <see cref="ICoalescingBuffer.SnapshotAndClear"/> and forwards each
/// surviving entry to the per-tenant queue as a
/// <see cref="PushIntent"/> (Sprint-5 plan U3, hand-off to U4 dispatcher).
/// </summary>
/// <remarks>
/// <para>Pattern mirrors Sprint-3-redux's
/// <c>PickWaveGeneratorService</c>: <see cref="PeriodicTimer"/> with the
/// injected <see cref="TimeProvider"/> so tests can advance the clock;
/// graceful shutdown on <see cref="OperationCanceledException"/>; one
/// try-catch boundary per tick so a single tenant's queue fault doesn't
/// kill the loop.</para>
///
/// <para>Tick latency: enqueue is non-blocking (queue impl is the
/// in-process <c>Channel&lt;T&gt;</c> bounded writer in U4), so the
/// flush completes in O(snapshot size). Snapshots beyond 10k entries are
/// unrealistic for one tenant's coalesce window — the buffer is bounded
/// by distinct <c>(tenant, sku, channel)</c> keys mutated in the
/// preceding 500ms.</para>
/// </remarks>
public sealed class CoalesceFlushService : BackgroundService
{
    private readonly ICoalescingBuffer _buffer;
    private readonly IPerTenantQueue _queue;
    private readonly StockSyncOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CoalesceFlushService> _logger;

    public CoalesceFlushService(
        ICoalescingBuffer buffer,
        IPerTenantQueue queue,
        IOptions<StockSyncOptions> options,
        TimeProvider clock,
        ILogger<CoalesceFlushService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _buffer = buffer;
        _queue = queue;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var window = TimeSpan.FromMilliseconds(_options.CoalesceWindowMs);
        _logger.LogInformation(
            "CoalesceFlushService started; window={WindowMs}ms",
            (int)window.TotalMilliseconds
        );

        using var timer = new PeriodicTimer(window, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        finally
        {
            _logger.LogInformation("CoalesceFlushService stopping.");
        }
    }

    /// <summary>
    /// Performs one drain + enqueue tick. Exposed for unit testing so
    /// tests don't pay the <see cref="PeriodicTimer"/> wall-clock cost.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        IReadOnlyList<KeyValuePair<CoalesceKey, CoalesceEntry>> snapshot;
        try
        {
            snapshot = _buffer.SnapshotAndClear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CoalesceFlushService failed to snapshot buffer this tick.");
            return;
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        foreach (var kv in snapshot)
        {
            try
            {
                var intent = new PushIntent(
                    TenantId: kv.Key.TenantId,
                    Sku: kv.Key.Sku,
                    ChannelType: kv.Key.ChannelType,
                    Available: kv.Value.AvailableToSell,
                    ObservedAt: kv.Value.ObservedAt,
                    IsFlashSale: kv.Value.IsFlashSale,
                    IdempotencyKey: PushIntent.BuildIdempotencyKey(
                        kv.Key.TenantId,
                        kv.Key.Sku,
                        kv.Key.ChannelType,
                        kv.Value.ObservedAt
                    )
                );
                await _queue.EnqueueAsync(intent, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "CoalesceFlushService enqueue failed for tenant={TenantId} sku={Sku} channel={ChannelType}; entry dropped.",
                    kv.Key.TenantId,
                    kv.Key.Sku,
                    kv.Key.ChannelType
                );
            }
        }

        _logger.LogDebug(
            "CoalesceFlushService flushed {Count} push intents at {FlushedAt:O}",
            snapshot.Count,
            _clock.GetUtcNow().UtcDateTime
        );
    }
}
