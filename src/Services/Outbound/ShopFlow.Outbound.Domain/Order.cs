using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Aggregate root for one customer order being fulfilled through the
/// Reserve → Pick → Pack → Ship pipeline per Sprint-3-redux plan R1-R3.
/// Carries the lifecycle <see cref="Status"/> mirror of the fulfillment
/// saga, the optional shipping label / tracking metadata recorded at
/// the carrier call (U6), and N <see cref="OrderLine"/> children each
/// reserving stock under a per-line id (composite UNIQUE on the
/// Inventory ledger per K10/K11).
/// </summary>
/// <remarks>
/// <para>U1 ships the type shape + private parameterless ctor for EF.
/// Factory <c>Create</c> + state-machine methods land in U2.</para>
///
/// <para>Inherits from <see cref="BaseEntity"/> (not <see cref="AggregateRoot"/>)
/// — mirrors <see cref="ShopFlow.Inventory.Domain.StockItem"/>'s rationale:
/// the inherited <c>byte[] RowVersion</c> on <c>AggregateRoot</c> doesn't
/// match the per-aggregate row-versioning shape we need. Outbound's
/// optimistic-concurrency token (when needed in Phase-2) will go on the
/// saga_state table managed by MassTransit's EF repo, not on Order itself
/// — the saga is the serialization point.</para>
///
/// <para>Per ADR-0003 no <c>tenant_id</c> column — the database identity
/// is the tenant boundary.</para>
/// </remarks>
public sealed class Order : BaseEntity
{
    public string ChannelExternalOrderId { get; private set; } = string.Empty;

    public string ShippingProfile { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; } = OrderStatus.Created;

    public int? ExpectedWeightTotal { get; private set; }

    public int? ActualWeightTotal { get; private set; }

    public string? LabelUrl { get; private set; }

    public string? TrackingNumber { get; private set; }

    public Guid? PickWaveId { get; private set; }

    private readonly List<OrderLine> _lines = new();

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    private Order() { }
}
