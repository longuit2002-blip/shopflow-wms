namespace ShopFlow.LoadTests.Stubs;

/// <summary>
/// Spec-only interface for the Phase-2 Sprint-5 stock-sync coalescer
/// (Plan §317). The real interface will live in
/// <c>ShopFlow.Channel.Application</c>; this in-test placeholder lets the
/// load harness encode the invariant before the implementation arrives.
/// </summary>
public interface IStockSyncCoalescer
{
    /// <summary>
    /// Enqueue a stock-change observation. Within the configured debounce
    /// window (default 500ms) only the latest value per
    /// <c>(tenant, sku, channel)</c> tuple is retained.
    /// </summary>
    void Enqueue(string key, int value);

    /// <summary>
    /// Drain the coalescer and return the count of outbound pushes that
    /// the implementation would have emitted for the current window.
    /// </summary>
    Task<int> FlushPushCountAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Stub coalescer that throws <see cref="NotImplementedException"/> from
/// every method. The NBomber scenario in
/// <c>SyncEnginePrimitivesTests.cs</c> catches the throw and records it as
/// the expected-stub-state in W1.
/// </summary>
public sealed class NotImplementedStockSyncCoalescer : IStockSyncCoalescer
{
    public const string StubMessagePrefix =
        "StockSyncCoalescer stub — Phase-2 Sprint-5 (W7) lands this; this stub is the spec, not the implementation.";

    public void Enqueue(string key, int value) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} Enqueue — see Plan §317 coalescing window."
        );

    public Task<int> FlushPushCountAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} FlushPushCountAsync — see Plan §317 push-count contract."
        );
}
