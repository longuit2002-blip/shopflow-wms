using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Child entity of <see cref="PickWave"/> per Sprint-3-redux plan R10.
/// One row per order assigned to the wave; the (PickWaveId, OrderId)
/// pair is unique by construction (U5 deduplicates at wave-close time
/// because each order produces exactly one <c>PickRequestV1</c> at
/// saga commit, and the per-tick buffer is keyed by order id implicitly
/// via the saga's at-most-once write).
/// </summary>
/// <remarks>
/// The internal <see cref="Create"/> factory exists because only the
/// parent <see cref="PickWave"/> should construct assignments — the
/// aggregate-root rule (AGENTS.md §4.20). EF Core uses the private
/// parameterless ctor for materialization.
/// </remarks>
public sealed class PickAssignment : BaseEntity
{
    public Guid PickWaveId { get; private set; }

    public Guid OrderId { get; private set; }

    private PickAssignment() { }

    /// <summary>
    /// Factory used by <see cref="PickWave.AssignOrder"/> — internal so
    /// callers must go through the aggregate root.
    /// </summary>
    internal static PickAssignment Create(Guid pickWaveId, Guid orderId, DateTime now)
    {
        return new PickAssignment
        {
            PickWaveId = pickWaveId,
            OrderId = orderId,
            CreatedAt = now,
        };
    }
}
