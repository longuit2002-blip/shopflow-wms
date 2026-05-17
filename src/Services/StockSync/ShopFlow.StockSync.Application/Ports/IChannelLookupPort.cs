namespace ShopFlow.StockSync.Application.Ports;

/// <summary>
/// Resolves the active channel slugs the StockSync fanout should mirror
/// to for a given tenant (Sprint-5 plan U3, R5 mirror-all).
/// </summary>
/// <remarks>
/// <para>Sprint-5 implementation reads the static
/// <c>StockSync:ActiveChannels</c> config list from
/// <see cref="ShopFlow.StockSync.Application.Options.StockSyncOptions.ActiveChannels"/>
/// — every tenant sees the same channels. Phase-3 swaps to a per-tenant
/// query against the Channel module's <c>channels</c> table so disabled
/// channels stop receiving pushes.</para>
///
/// <para>The port intentionally returns slugs (matching
/// <c>CoalesceKey.ChannelType</c>) rather than full Channel aggregates —
/// the dispatcher resolves the concrete <c>IChannelAdapter</c> downstream
/// via <c>IChannelAdapterFactory</c> at push time (Sprint-5 U5).</para>
/// </remarks>
public interface IChannelLookupPort
{
    /// <summary>
    /// Returns the active channel slugs for <paramref name="tenantId"/>.
    /// Empty array means "no channels enabled" — the consumer skips fanout
    /// without faulting.
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveChannelsAsync(Guid tenantId, CancellationToken ct);
}
