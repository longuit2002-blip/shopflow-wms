namespace ShopFlow.StockSync.Application.Coalescing;

/// <summary>
/// In-memory buffer that collapses repeated <c>StockLevelChangedV1</c>
/// observations per <see cref="CoalesceKey"/> down to a single latest
/// reading inside a flush window. The implementation is process-singleton
/// (Sprint-5 plan KTD4) — host restart loses unflushed entries, which is
/// acceptable because Inventory re-emits on the next mutation.
/// </summary>
public interface ICoalescingBuffer
{
    /// <summary>
    /// Records the latest reading for <paramref name="key"/>. If a value
    /// already exists for the key, the incoming
    /// <paramref name="entry"/> is kept only when its
    /// <see cref="CoalesceEntry.ObservedAt"/> is &gt;= the existing one —
    /// out-of-order MassTransit redeliveries can't regress published stock.
    /// Thread-safe; callable from any consumer thread.
    /// </summary>
    void Upsert(CoalesceKey key, CoalesceEntry entry);

    /// <summary>
    /// Atomically removes every entry from the buffer and returns the
    /// drained pairs. Called by <c>CoalesceFlushService</c> on each
    /// <c>PeriodicTimer</c> tick. The returned list is a snapshot — later
    /// <see cref="Upsert"/> calls won't mutate it.
    /// </summary>
    IReadOnlyList<KeyValuePair<CoalesceKey, CoalesceEntry>> SnapshotAndClear();

    /// <summary>
    /// Current count of distinct keys held. Exposed for diagnostics and
    /// unit testing the concurrent-write invariant.
    /// </summary>
    int Count { get; }
}
