namespace ShopFlow.LoadTests.Stubs;

/// <summary>
/// Spec-only interface for the Phase-2 Sprint-5 per-channel rate limiter
/// (Plan §318). Token bucket sized to the marketplace's published rate
/// limit. The real interface will live in
/// <c>ShopFlow.Channel.Application</c>; this in-test placeholder lets the
/// load harness encode the invariant before the implementation arrives.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Acquire a single token. Returns the number of tokens served by the
    /// current bucket on this call (1 if served, 0 if throttled). The real
    /// implementation may also delay-and-serve; the NBomber scenario only
    /// asserts the served count over the run duration.
    /// </summary>
    ValueTask<int> AcquireAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Stub limiter that throws <see cref="NotImplementedException"/> from
/// every method. The NBomber scenario in
/// <c>SyncEnginePrimitivesTests.cs</c> catches the throw and records it as
/// the expected-stub-state in W1.
/// </summary>
public sealed class NotImplementedRateLimiter : IRateLimiter
{
    public const string StubMessagePrefix =
        "RateLimiter stub — Phase-2 Sprint-5 (W7) lands this; this stub is the spec, not the implementation.";

    public ValueTask<int> AcquireAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} AcquireAsync — see Plan §318 token-bucket contract."
        );
}
