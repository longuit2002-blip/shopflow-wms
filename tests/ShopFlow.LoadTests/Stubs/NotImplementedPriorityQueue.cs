namespace ShopFlow.LoadTests.Stubs;

/// <summary>
/// Spec-only interface for the Phase-2 Sprint-5 priority queue (Plan
/// §319). Flash-sale SKUs preempt regular SKUs. The real interface will
/// live in <c>ShopFlow.Channel.Application</c>; this in-test placeholder
/// lets the load harness encode the invariant before the implementation
/// arrives.
/// </summary>
public interface IPriorityQueue<T>
{
    /// <summary>
    /// Enqueue <paramref name="item"/> at the supplied priority. Higher
    /// numeric priority preempts lower; ties are FIFO.
    /// </summary>
    ValueTask EnqueueAsync(T item, int priority, CancellationToken cancellationToken);

    /// <summary>
    /// Stream items in priority-then-FIFO order. The async sequence ends
    /// when the queue is drained or the token is cancelled.
    /// </summary>
    IAsyncEnumerable<T> DequeueAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Stub queue that throws <see cref="NotImplementedException"/> from every
/// method. The NBomber scenario in <c>SyncEnginePrimitivesTests.cs</c>
/// catches the throw and records it as the expected-stub-state in W1.
/// </summary>
public sealed class NotImplementedPriorityQueue<T> : IPriorityQueue<T>
{
    public const string StubMessagePrefix =
        "PriorityQueue stub — Phase-2 Sprint-5 (W7) lands this; this stub is the spec, not the implementation.";

    public ValueTask EnqueueAsync(T item, int priority, CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} EnqueueAsync — see Plan §319 priority preemption."
        );

    public IAsyncEnumerable<T> DequeueAllAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} DequeueAllAsync — see Plan §319 priority preemption."
        );
}
