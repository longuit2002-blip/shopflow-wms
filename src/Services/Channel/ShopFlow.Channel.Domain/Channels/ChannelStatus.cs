namespace ShopFlow.Channel.Domain.Channels;

/// <summary>
/// Lifecycle states of a tenant-side <see cref="Channel"/> projection per
/// Sprint-4 plan R1. The control-plane <c>channel_connections</c> row is the
/// source of truth for who-owns-the-channel; the tenant-DB <c>channels</c>
/// row is a denormalized adapter-routing projection that toggles between
/// <c>Active</c> (webhooks accepted, sync engine pushes stock) and
/// <c>Disabled</c> (webhooks return 200 no-op, sync engine skips). One-way
/// in MVP; rotation back to Active is an admin operation in Sprint-7+.
/// </summary>
public enum ChannelStatus
{
    Active = 0,
    Disabled = 1,
}
