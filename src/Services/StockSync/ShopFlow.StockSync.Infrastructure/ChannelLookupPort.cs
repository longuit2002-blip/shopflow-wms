using Microsoft.Extensions.Options;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.Infrastructure;

/// <summary>
/// Sprint-5 plan U8 — static <see cref="IChannelLookupPort"/> implementation
/// that returns the <c>StockSync:ActiveChannels</c> config list verbatim for
/// every tenant. Sufficient for the Sprint-5 portfolio scope; Phase-3 swaps
/// in a per-tenant query against the Channel module's <c>channels</c> table
/// so disabled connections drop out of the fanout.
/// </summary>
/// <remarks>
/// <para>Singleton lifetime: the underlying
/// <see cref="StockSyncOptions.ActiveChannels"/> array is bound from
/// configuration at startup and never mutated. The port is on the hot
/// consume path (one call per <c>StockLevelChangedV1</c>), so paying a
/// scope-creation cost would be wasteful.</para>
///
/// <para>The <paramref name="tenantId"/> parameter is honored only for
/// forward compatibility — Phase-3 will use it to look up
/// <c>(tenant_id, channel_type, is_enabled)</c> tuples. Sprint-5 ignores
/// it.</para>
/// </remarks>
public sealed class ChannelLookupPort : IChannelLookupPort
{
    private readonly IReadOnlyList<string> _activeChannels;

    public ChannelLookupPort(IOptions<StockSyncOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _activeChannels = options.Value.ActiveChannels ?? Array.Empty<string>();
    }

    public Task<IReadOnlyList<string>> GetActiveChannelsAsync(
        Guid tenantId,
        CancellationToken ct
    ) => Task.FromResult(_activeChannels);
}
