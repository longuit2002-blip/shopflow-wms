namespace ShopFlow.Contracts.Outbound;

/// <summary>
/// Emitted by the Outbound module's saga (Sprint-3-redux U6) after the
/// mock shipping provider returns a label + tracking number. Phase-2's
/// Channel module will consume this to push the tracking info back to the
/// marketplace; Sprint-3-redux ships a stub <c>ChannelTrackingConsumer</c>
/// per K9.
/// </summary>
/// <remarks>
/// <see cref="ChannelId"/> is a placeholder for Phase-2 — null in
/// Sprint-3-redux because the order is persisted with the external
/// channel id but the per-tenant channel-connection routing hasn't
/// shipped yet. Phase-2 W6 wires this up.
/// </remarks>
public sealed record TrackingPushedV1(
    Guid OrderId,
    Guid TenantId,
    string TrackingNumber,
    string LabelUrl,
    string? ChannelId,
    DateTime OccurredAt
);
