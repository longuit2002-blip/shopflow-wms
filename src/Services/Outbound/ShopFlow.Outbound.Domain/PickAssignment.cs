using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Child entity of <see cref="PickWave"/> per Sprint-3-redux plan R10.
/// One row per order assigned to the wave; the (PickWaveId, OrderId)
/// pair is unique by construction (U5 deduplicates at wave-close time).
/// </summary>
/// <remarks>
/// U1 ships the type shape only. <c>Create</c> + relationship wiring
/// land in U5.
/// </remarks>
public sealed class PickAssignment : BaseEntity
{
    public Guid PickWaveId { get; private set; }

    public Guid OrderId { get; private set; }

    private PickAssignment() { }
}
