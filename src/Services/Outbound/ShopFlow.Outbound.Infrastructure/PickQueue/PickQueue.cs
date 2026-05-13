using System.Collections.Concurrent;
using System.Threading.Channels;
using ShopFlow.Outbound.Application;
using ShopFlow.Outbound.Application.Ports;

namespace ShopFlow.Outbound.Infrastructure.PickQueue;

/// <summary>
/// Sprint-3-redux U5 K3 — <see cref="IPickQueue"/> implementation backed
/// by a <see cref="ConcurrentDictionary{TKey, TValue}"/> of bounded
/// <see cref="Channel{T}"/> instances keyed by tenant id. Per the K3
/// design decision <c>GetOrAdd</c> creates the per-tenant channel
/// lazily on first writer/reader access; capacity is 1000 with
/// <c>FullMode = BoundedChannelFullMode.Wait</c>, so saga writes
/// back-pressure when the queue fills (correctness over latency per the
/// project's hard non-negotiable).
/// </summary>
/// <remarks>
/// <para>Registered as <c>Singleton</c> in <c>AddOutboundModule</c> so
/// one channel registry survives across consume scopes — every saga
/// commit + every generator tick share the same per-tenant channels.</para>
///
/// <para><c>SingleReader = true</c> is correct because the wave
/// generator is single-instance per Phase-1 modular monolith host.
/// <c>SingleWriter = false</c> because multiple saga consume scopes (and
/// any future ad-hoc enqueuers) may publish concurrently.</para>
/// </remarks>
public sealed class PickQueue : IPickQueue
{
    private const int Capacity = 1000;

    private readonly ConcurrentDictionary<Guid, Channel<PickRequestV1>> _byTenant = new();

    public ChannelWriter<PickRequestV1> GetWriter(Guid tenantId) => GetOrCreate(tenantId).Writer;

    public ChannelReader<PickRequestV1> GetReader(Guid tenantId) => GetOrCreate(tenantId).Reader;

    private Channel<PickRequestV1> GetOrCreate(Guid tenantId)
    {
        return _byTenant.GetOrAdd(
            tenantId,
            static _ =>
                Channel.CreateBounded<PickRequestV1>(
                    new BoundedChannelOptions(Capacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false,
                    }
                )
        );
    }
}
