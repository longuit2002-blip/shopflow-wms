using System.Collections.Concurrent;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.StockSync.IntegrationTests.Drivers;

/// <summary>
/// Sprint-5 U9 — in-test <see cref="IChannelAdapterFactory"/> override
/// for the StockSync integration suite. The real
/// <c>ChannelAdapterFactory</c> + <c>ShopeeAdapter</c> live in the
/// Channel module's Infrastructure project, which the StockSync.Api
/// project graph does NOT reference (StockSync only pulls in
/// <c>Channel.Application</c> for the <see cref="IChannelAdapter"/>
/// interface). U8's <c>AddStockSyncModule</c> consequently leaves
/// <see cref="IChannelAdapterFactory"/> unregistered — the production
/// host (Aspire AppHost W6 split) will compose it via a peer
/// <c>AddChannelModule</c> call. For the U9 integration test, we
/// register this fake in the <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{Program}"/>
/// service override so the <c>PerTenantDispatcherService</c> can run
/// end-to-end without booting the Channel module's full schema +
/// HttpClient stack.
/// </summary>
/// <remarks>
/// <para>The fake records every <see cref="StockUpdateRequest"/> the
/// dispatcher hands it, indexed by channel type. Tests inspect
/// <see cref="Pushes"/> to assert the push body matched the
/// <c>StockLevelChangedV1</c> input. Push behaviour is configurable
/// via <see cref="FailNextPush"/> + <see cref="ResetFailure"/> for the
/// breaker-recovery flow.</para>
///
/// <para>The fake's <see cref="IChannelAdapter.ParseWebhook"/> and
/// <see cref="IChannelAdapter.ParseOrderCreated"/> implementations are
/// intentional no-ops — U9 exercises the push path only.</para>
/// </remarks>
public sealed class FakeChannelAdapterFactory : IChannelAdapterFactory
{
    private readonly ConcurrentDictionary<string, FakeChannelAdapter> _adapters = new(
        StringComparer.OrdinalIgnoreCase
    );

    public FakeChannelAdapterFactory(params string[] channelTypes)
    {
        if (channelTypes.Length == 0)
        {
            channelTypes = new[] { "shopee" };
        }
        foreach (var t in channelTypes)
        {
            _adapters[t] = new FakeChannelAdapter(t);
        }
    }

    /// <summary>
    /// All pushes recorded across every channel since the factory was
    /// constructed. Thread-safe for concurrent dispatcher writers + the
    /// test-side reader.
    /// </summary>
    public IEnumerable<RecordedPush> Pushes => _adapters.Values.SelectMany(a => a.Pushes);

    /// <summary>Snapshot of pushes for one channel type.</summary>
    public IReadOnlyList<RecordedPush> PushesFor(string channelType) =>
        _adapters.TryGetValue(channelType, out var a)
            ? a.PushesSnapshot()
            : Array.Empty<RecordedPush>();

    public IChannelAdapter ResolveFor(string channelType) =>
        _adapters.TryGetValue(channelType, out var a)
            ? a
            : throw new UnknownChannelTypeException(channelType);

    public IChannelAdapter? TryResolve(string channelType) =>
        _adapters.TryGetValue(channelType, out var a) ? a : null;

    /// <summary>
    /// Switch the named channel's push behaviour to a Result.Failure with
    /// <paramref name="errorCode"/>. Used by breaker-recovery scenarios to
    /// inject a stream of failures that trip the per-tenant breaker.
    /// </summary>
    public void FailNextPush(string channelType, string errorCode = "shopee.push.5xx")
    {
        if (_adapters.TryGetValue(channelType, out var a))
        {
            a.FailWith = errorCode;
        }
    }

    /// <summary>Restore the named channel's push behaviour to success.</summary>
    public void ResetFailure(string channelType)
    {
        if (_adapters.TryGetValue(channelType, out var a))
        {
            a.FailWith = null;
        }
    }
}

/// <summary>
/// Recorded push observation — what the dispatcher handed the adapter for
/// one <see cref="StockUpdateRequest"/>.
/// </summary>
public sealed record RecordedPush(
    string ChannelType,
    Guid ChannelId,
    string ExternalSku,
    int Quantity,
    DateTime ObservedAt,
    string IdempotencyKey,
    DateTime PushedAt
);

/// <summary>
/// Inner adapter wired by <see cref="FakeChannelAdapterFactory"/>.
/// Records every <see cref="PushStockUpdateAsync"/> call into a thread-safe
/// list; configurable failure injection via <see cref="FailWith"/>.
/// </summary>
internal sealed class FakeChannelAdapter : IChannelAdapter
{
    private readonly List<RecordedPush> _pushes = new();
    private readonly object _gate = new();

    public FakeChannelAdapter(string channelType)
    {
        ChannelType = channelType;
    }

    public string ChannelType { get; }

    /// <summary>
    /// When non-null, the next push returns <see cref="Result.Failure"/>
    /// with this error code (and is still recorded). Setting this lets
    /// breaker-recovery tests force the trip without rebuilding the host.
    /// </summary>
    public string? FailWith { get; set; }

    public IEnumerable<RecordedPush> Pushes
    {
        get
        {
            lock (_gate)
            {
                return _pushes.ToArray();
            }
        }
    }

    public IReadOnlyList<RecordedPush> PushesSnapshot()
    {
        lock (_gate)
        {
            return _pushes.ToArray();
        }
    }

    public Result<WebhookEnvelope> ParseWebhook(
        Guid channelId,
        ReadOnlySpan<byte> body,
        IReadOnlyDictionary<string, string> headers
    ) =>
        Result<WebhookEnvelope>.Failure(
            "Fake adapter does not parse webhooks.",
            "fake.adapter.parsewebhook_unsupported"
        );

    public Result<ExternalOrderDraft> ParseOrderCreated(WebhookEnvelope envelope) =>
        Result<ExternalOrderDraft>.Failure(
            "Fake adapter does not parse orders.",
            "fake.adapter.parseorder_unsupported"
        );

    public Task<Result> PushStockUpdateAsync(StockUpdateRequest request, CancellationToken ct)
    {
        var pushedAt = DateTime.UtcNow;
        var recorded = new RecordedPush(
            ChannelType: ChannelType,
            ChannelId: request.ChannelId,
            ExternalSku: request.ExternalSku,
            Quantity: request.Quantity,
            ObservedAt: request.ObservedAt,
            IdempotencyKey: request.IdempotencyKey,
            PushedAt: pushedAt
        );
        lock (_gate)
        {
            _pushes.Add(recorded);
        }

        if (FailWith is { } code)
        {
            return Task.FromResult(Result.Failure($"Fake push failure for {ChannelType}.", code));
        }

        return Task.FromResult(Result.Success());
    }
}
