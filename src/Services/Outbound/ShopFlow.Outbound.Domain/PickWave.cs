using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Aggregate root for one closed pick wave per Sprint-3-redux plan R10.
/// The <see cref="ShopFlow.Outbound.Infrastructure.Workers"/>
/// <c>PickWaveGeneratorService</c> (U5) drains per-tenant
/// <c>Channel&lt;PickRequestV1&gt;</c> queues with 15-min sliding-window
/// batching grouped by <c>(tenant_id, shipping_profile)</c>; each
/// closed group materialises one <see cref="PickWave"/> with N
/// <see cref="PickAssignment"/> child rows + round-robin
/// <see cref="PickerId"/>.
/// </summary>
/// <remarks>
/// U1 ships the type shape only. <c>Create</c> + close-window logic land
/// in U5.
/// </remarks>
public sealed class PickWave : BaseEntity
{
    public string ShippingProfile { get; private set; } = string.Empty;

    public string PickerId { get; private set; } = string.Empty;

    public DateTime? ClosedAt { get; private set; }

    private readonly List<PickAssignment> _assignments = new();

    public IReadOnlyList<PickAssignment> Assignments => _assignments.AsReadOnly();

    private PickWave() { }
}
