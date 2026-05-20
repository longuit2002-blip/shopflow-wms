using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Options;

namespace ShopFlow.StockSync.Infrastructure.Dispatch;

/// <summary>
/// Sprint-5 plan U4 — <see cref="IPerTenantQueue"/> implementation
/// backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/> of
/// per-tenant <see cref="TenantQueuePair"/> entries, each holding two
/// bounded <see cref="Channel{T}"/> instances: one high-priority lane
/// (flash-sale traffic) and one normal-priority lane (baseline mirror
/// traffic).
/// </summary>
/// <remarks>
/// <para>Per the K3 / R10 design decisions the lanes use
/// <see cref="BoundedChannelFullMode.DropOldest"/>: coalescing in U3
/// already collapsed redundant updates, so dropping the oldest pending
/// intent is a safe trade-off — the latest reading still wins, the
/// dispatcher just sees fewer redundant pushes. Observability counter
/// for drops surfaces via U8 diagnostics.</para>
///
/// <para>Per-tenant lanes are created lazily on the first
/// <see cref="EnqueueAsync"/> for that tenant via
/// <c>ConcurrentDictionary.GetOrAdd</c>. <c>SingleReader = true</c>
/// because the U5 dispatcher BackgroundService is single-instance per
/// tenant; <c>SingleWriter = false</c> because multiple flush ticks +
/// any future ad-hoc enqueuers may publish concurrently.</para>
///
/// <para>Registered as <c>Singleton</c> in <c>AddStockSyncModule</c>
/// (U8) so one registry survives across consume scopes — every flush
/// tick + every dispatcher loop share the same per-tenant channels.</para>
/// </remarks>
public sealed class PerTenantQueue : IPerTenantQueue
{
    private readonly ConcurrentDictionary<Guid, TenantQueuePair> _byTenant = new();
    private readonly int _highCap;
    private readonly int _normalCap;

    public PerTenantQueue(IOptions<StockSyncOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capacity = options.Value.QueueCapacity;
        _highCap = capacity.HighCap;
        _normalCap = capacity.NormalCap;
    }

    public ValueTask EnqueueAsync(PushIntent intent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var pair = GetOrCreate(intent.TenantId);
        var writer = intent.IsFlashSale ? pair.High.Writer : pair.Normal.Writer;
        return writer.WriteAsync(intent, ct);
    }

    public async ValueTask<PushIntent> ReadNextAsync(Guid tenantId, CancellationToken ct)
    {
        var pair = GetOrCreate(tenantId);
        var highReader = pair.High.Reader;
        var normalReader = pair.Normal.Reader;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Strict priority — drain anything already pending in the
            // high lane before considering the normal lane.
            if (highReader.TryRead(out var hi))
            {
                return hi;
            }

            if (normalReader.TryRead(out var lo))
            {
                return lo;
            }

            // Both lanes empty — wait for whichever signals first.
            // After wake-up, loop back to TryRead-high first so the
            // high lane keeps strict priority even if both lanes
            // became readable in the same scheduler tick.
            var highWait = highReader.WaitToReadAsync(ct).AsTask();
            var normalWait = normalReader.WaitToReadAsync(ct).AsTask();
            await Task.WhenAny(highWait, normalWait).ConfigureAwait(false);
        }
    }

    private TenantQueuePair GetOrCreate(Guid tenantId)
    {
        return _byTenant.GetOrAdd(
            tenantId,
            static (_, state) => TenantQueuePair.Create(state.highCap, state.normalCap),
            (highCap: _highCap, normalCap: _normalCap)
        );
    }

    /// <summary>
    /// Holds the two bounded <see cref="Channel{T}"/> instances for one
    /// tenant — high-priority lane (flash-sale traffic) and
    /// normal-priority lane (baseline traffic).
    /// </summary>
    private sealed record TenantQueuePair(
        Channel<PushIntent> High,
        Channel<PushIntent> Normal
    )
    {
        public static TenantQueuePair Create(int highCap, int normalCap)
        {
            var high = System.Threading.Channels.Channel.CreateBounded<PushIntent>(
                new BoundedChannelOptions(highCap)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                }
            );

            var normal = System.Threading.Channels.Channel.CreateBounded<PushIntent>(
                new BoundedChannelOptions(normalCap)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                }
            );

            return new TenantQueuePair(high, normal);
        }
    }
}
