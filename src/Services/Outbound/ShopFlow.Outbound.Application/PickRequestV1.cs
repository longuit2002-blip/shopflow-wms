namespace ShopFlow.Outbound.Application;

/// <summary>
/// In-process envelope written by the <c>FulfillmentSaga</c>'s
/// <c>StockReserved</c> Then-handler to the per-tenant
/// <c>Channel&lt;PickRequestV1&gt;</c> behind
/// <see cref="Ports.IPickQueue"/>. The <c>PickWaveGeneratorService</c>
/// (U5) drains the channel each tick and groups items by
/// <c>(tenant_id, shipping_profile)</c> for wave emission.
/// </summary>
/// <remarks>
/// <para>NOT a MassTransit cross-module contract — this type intentionally
/// lives in the Application namespace, NOT
/// <c>ShopFlow.Contracts.Outbound</c>. The channel write is in-process
/// only; nothing on the bus carries this record. Per the K4 design
/// decision the wave generator is single-instance per Phase-1 (Aspire
/// AppHost), so an in-memory channel is sufficient and avoids the extra
/// outbox hop.</para>
///
/// <para><see cref="EnqueuedAt"/> is captured at saga commit time and
/// drives the 15-min sliding window aging logic in
/// <c>PickWaveGeneratorService</c>; <see cref="LineCount"/> is carried
/// for diagnostic / observability use (e.g., wave-fullness metrics).</para>
/// </remarks>
/// <param name="OrderId">The Outbound <c>orders.id</c> for the reserved order.</param>
/// <param name="TenantId">Tenant the order belongs to — must match the channel key.</param>
/// <param name="ShippingProfile">Drives wave grouping per plan AE4 ("standard", "express", ...).</param>
/// <param name="EnqueuedAt">UTC timestamp when the saga emitted the request; ages the window.</param>
/// <param name="LineCount">Number of order lines (diagnostic only; Phase-2 wave-fullness metrics).</param>
public sealed record PickRequestV1(
    Guid OrderId,
    Guid TenantId,
    string ShippingProfile,
    DateTime EnqueuedAt,
    int LineCount
);
