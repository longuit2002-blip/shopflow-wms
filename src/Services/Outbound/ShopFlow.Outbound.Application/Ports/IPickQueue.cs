using System.Threading.Channels;

namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Per-tenant in-process queue of <see cref="PickRequestV1"/> envelopes
/// per Sprint-3-redux U5 K3. Backed by a
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// keyed by tenant id, each value a bounded
/// <see cref="System.Threading.Channels.Channel{T}"/> with capacity
/// 1000 and <c>FullMode = BoundedChannelFullMode.Wait</c>. Saga writers
/// experience back-pressure when the per-tenant channel is full —
/// correctness wins over latency per the hard non-negotiable.
/// </summary>
/// <remarks>
/// <para>Lazy creation: <see cref="GetWriter"/> / <see cref="GetReader"/>
/// allocate the channel on first access via <c>GetOrAdd</c>. New tenants
/// added to the catalog get a channel on first saga write; no manual
/// registration step.</para>
///
/// <para>Memory profile: each channel is bounded at 1000 × per-tenant ×
/// envelope size (~40 bytes). For 1000 tenants the worst case is
/// ~40 MB sustained, which the Phase-1 modular monolith host can
/// trivially absorb. Phase-2's multi-instance leader-election work will
/// need a per-tenant channel cleanup path when tenants archive; tracked
/// in the plan's risk row.</para>
/// </remarks>
public interface IPickQueue
{
    /// <summary>
    /// Producer-side handle for <paramref name="tenantId"/>'s channel.
    /// The saga calls <c>WriteAsync</c> (or <c>TryWrite</c>) on this.
    /// </summary>
    ChannelWriter<PickRequestV1> GetWriter(Guid tenantId);

    /// <summary>
    /// Consumer-side handle for <paramref name="tenantId"/>'s channel.
    /// The wave generator drains with <c>TryRead</c> per tick.
    /// </summary>
    ChannelReader<PickRequestV1> GetReader(Guid tenantId);
}
