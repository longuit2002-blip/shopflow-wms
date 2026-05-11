namespace ShopFlow.ControlPlane.Application.Ports;

/// <summary>
/// Read-only port over <c>shopflow_control.channel_connections</c>. Inbound
/// webhook receivers (Shopee, Lazada, TikTok Shop, Shopify) carry a
/// <c>channel_id</c> on the request; the receiver looks the row up here to
/// resolve the owning tenant before routing into that tenant's DB.
/// </summary>
/// <remarks>
/// <para>Cache discipline mirrors <c>ITenantCatalog</c>: in-memory LRU
/// (size 1000, TTL 5 min), synchronous eviction on write paths (channel
/// connect / disconnect). The Channel module is the only intended consumer
/// today; the seam exists at this layer so that future modules (e.g., a
/// future Marketplace Onboarding service) can route without re-implementing
/// the lookup.</para>
/// <para>Resolution returns <see cref="ChannelTenantBinding"/> rather than a
/// raw tenant id so the receiver can fetch the full tenant view via
/// <c>ITenantCatalog.LookupByIdAsync</c> in a single round-trip (no JOIN
/// across catalog tables — the catalog cache absorbs the second hit).</para>
/// </remarks>
public interface IChannelDirectory
{
    /// <summary>
    /// Resolve a channel to its tenant. Returns <c>null</c> when the channel
    /// is unknown (route returns 404; never silently accepts the webhook).
    /// </summary>
    Task<ChannelTenantBinding?> LookupAsync(Guid channelId, CancellationToken ct);
}

/// <summary>
/// Read projection of one <c>channel_connections</c> row. The
/// <see cref="SecretEncrypted"/> blob is opaque to consumers; signature
/// verification routes through the (future) KMS unwrap workflow.
/// </summary>
public sealed record ChannelTenantBinding(
    Guid ChannelId,
    Guid TenantId,
    string ChannelType,
    byte[] SecretEncrypted
);
