using System.Collections.Concurrent;

namespace ShopFlow.StockSync.Application.Coalescing;

/// <summary>
/// <see cref="ICoalescingBuffer"/> implementation backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>. Sprint-5 plan KTD4 —
/// stay on built-in .NET 9 primitives; no extra dependency.
/// </summary>
/// <remarks>
/// <para>Registered as a singleton: a single buffer instance serves every
/// <c>StockLevelChangedConsumer</c> message + the matching
/// <c>CoalesceFlushService</c>. Thread-safety is supplied entirely by the
/// underlying <c>ConcurrentDictionary</c>; no extra lock.</para>
///
/// <para>The <see cref="Upsert"/> path uses
/// <c>AddOrUpdate(key, addFactory, updateFactory)</c> so the
/// out-of-order check + value swap is atomic per-key — concurrent writers
/// for the same key never observe a partially-overwritten entry.</para>
///
/// <para><see cref="SnapshotAndClear"/> walks the keys collected by
/// <c>ToArray()</c> (a snapshot at the moment of the call) and removes
/// each via <c>TryRemove</c>. Entries that arrive between the snapshot
/// and the removal stay in the buffer for the next flush — drains are
/// best-effort per tick but never lose data, only delay it by one
/// window.</para>
/// </remarks>
public sealed class CoalescingBuffer : ICoalescingBuffer
{
    private readonly ConcurrentDictionary<CoalesceKey, CoalesceEntry> _entries = new();

    public int Count => _entries.Count;

    public void Upsert(CoalesceKey key, CoalesceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.AddOrUpdate(
            key,
            addValueFactory: static (_, incoming) => incoming,
            updateValueFactory: static (_, existing, incoming) =>
                incoming.ObservedAt >= existing.ObservedAt ? incoming : existing,
            factoryArgument: entry
        );
    }

    public IReadOnlyList<KeyValuePair<CoalesceKey, CoalesceEntry>> SnapshotAndClear()
    {
        // Snapshot of (key, value) pairs as of this instant. Entries that
        // land after this line stay in the buffer for the next tick.
        var snapshot = _entries.ToArray();
        if (snapshot.Length == 0)
        {
            return Array.Empty<KeyValuePair<CoalesceKey, CoalesceEntry>>();
        }

        var result = new List<KeyValuePair<CoalesceKey, CoalesceEntry>>(snapshot.Length);
        foreach (var kv in snapshot)
        {
            // Only include keys we actually own + remove. If a concurrent
            // tick already drained a key, skip it.
            if (_entries.TryRemove(new KeyValuePair<CoalesceKey, CoalesceEntry>(kv.Key, kv.Value)))
            {
                result.Add(kv);
                continue;
            }

            // The value moved on between our snapshot and the remove —
            // try once with the current value so the latest reading still
            // flushes this tick.
            if (_entries.TryGetValue(kv.Key, out var current)
                && _entries.TryRemove(new KeyValuePair<CoalesceKey, CoalesceEntry>(kv.Key, current)))
            {
                result.Add(new KeyValuePair<CoalesceKey, CoalesceEntry>(kv.Key, current));
            }
        }
        return result;
    }
}
