using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Aggregate root for one closed pick wave per Sprint-3-redux plan R10.
/// The <c>PickWaveGeneratorService</c> (U5) drains per-tenant
/// <c>Channel&lt;PickRequestV1&gt;</c> queues with 15-min sliding-window
/// batching grouped by <c>(tenant_id, shipping_profile)</c>; each
/// closed group materialises one <see cref="PickWave"/> with N
/// <see cref="PickAssignment"/> child rows + round-robin
/// <see cref="PickerId"/>.
/// </summary>
/// <remarks>
/// <para>Per Sprint-3-redux K4 the wave-close logic lives in the
/// generator (time + size triggers); the aggregate is a passive carrier
/// of the closed wave's state. <see cref="Open"/> factory + the two
/// state-mutation methods (<see cref="AssignOrder"/> and
/// <see cref="Close"/>) are the only ways to mutate state — direct
/// public setters are forbidden so the closed-wave invariant holds.</para>
///
/// <para>Inherits <see cref="BaseEntity"/> (no <c>byte[] RowVersion</c>) —
/// concurrency control on the wave row is unnecessary because each wave
/// is materialised exactly once by the single-instance
/// <c>PickWaveGeneratorService</c>; the wave is never updated after
/// <see cref="Close"/>. Phase-2 multi-instance leader election will
/// re-examine this assumption.</para>
///
/// <para>Per ADR-0003 no <c>tenant_id</c> column — the database identity
/// is the tenant boundary.</para>
/// </remarks>
public sealed class PickWave : BaseEntity
{
    public string ShippingProfile { get; private set; } = string.Empty;

    public string PickerId { get; private set; } = string.Empty;

    public DateTime? ClosedAt { get; private set; }

    private readonly List<PickAssignment> _assignments = new();

    public IReadOnlyList<PickAssignment> Assignments => _assignments.AsReadOnly();

    private PickWave() { }

    /// <summary>
    /// Factory for a fresh, open pick wave grouped under one
    /// <paramref name="shippingProfile"/> and pre-assigned to
    /// <paramref name="pickerId"/> by the wave generator's round-robin
    /// cursor.
    /// </summary>
    public static PickWave Open(string shippingProfile, string pickerId, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(shippingProfile))
        {
            throw new ArgumentException(
                "shipping_profile is required.",
                nameof(shippingProfile)
            );
        }
        if (string.IsNullOrWhiteSpace(pickerId))
        {
            throw new ArgumentException("picker_id is required.", nameof(pickerId));
        }

        return new PickWave
        {
            ShippingProfile = shippingProfile.Trim(),
            PickerId = pickerId.Trim(),
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// Append one order to the wave's assignment list. Caller is
    /// responsible for not double-assigning — at the U5 generator level
    /// the per-tick buffer dedupes by <see cref="PickRequestV1.OrderId"/>
    /// naturally because each order produces exactly one PickRequest at
    /// saga commit.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the wave is already closed.</exception>
    public Result AssignOrder(Guid orderId, DateTime now)
    {
        if (ClosedAt.HasValue)
        {
            return Result.Failure(
                "cannot assign to a closed wave.",
                "pick_wave.closed"
            );
        }
        if (orderId == Guid.Empty)
        {
            return Result.Failure("order_id is required.", "pick_wave.order_id_required");
        }

        _assignments.Add(PickAssignment.Create(Id, orderId, now));
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Close the wave; subsequent <see cref="AssignOrder"/> calls fail.
    /// </summary>
    public Result Close(DateTime closedAt)
    {
        if (ClosedAt.HasValue)
        {
            return Result.Failure(
                "wave already closed.",
                "pick_wave.already_closed"
            );
        }
        ClosedAt = closedAt;
        UpdatedAt = closedAt;
        return Result.Success();
    }
}
