using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.ControlPlane.Domain;

/// <summary>
/// Channel-to-tenant routing row per Tech Design v3.0 §1.5. Inbound webhook
/// receivers (Shopee, Lazada, TikTok Shop, Shopify) carry only a
/// <c>channel_id</c> in the payload; the webhook layer looks the row up in
/// <c>shopflow_control.channel_connections</c> via
/// <see cref="ControlPlane.Application.Ports.IChannelDirectory"/> to resolve
/// the tenant before routing into the correct tenant DB.
/// </summary>
/// <remarks>
/// <c>SecretEncrypted</c> is the channel's HMAC signing secret, encrypted
/// at rest. Phase-0-redux ships the column shape only; the actual KMS-backed
/// encryption is a Phase-2 deliverable per plan §scope-boundaries.
/// </remarks>
public sealed class ChannelConnection : BaseEntity
{
    /// <summary>
    /// Channel identifier as it appears on inbound webhooks. Primary key so
    /// the directory lookup is an index hit; the
    /// <see cref="BaseEntity.Id"/> field is shadowed by this property in the
    /// EF configuration.
    /// </summary>
    public Guid ChannelId { get; private set; }

    public Guid TenantId { get; private set; }

    public string ChannelType { get; private set; } = string.Empty;

    public byte[] SecretEncrypted { get; private set; } = Array.Empty<byte>();

    private ChannelConnection() { }

    public static Result<ChannelConnection> Create(
        Guid channelId,
        Guid tenantId,
        string channelType,
        byte[] secretEncrypted
    )
    {
        if (channelId == Guid.Empty)
        {
            return Result<ChannelConnection>.Failure(
                "channel_id is required",
                "channel.channel_id_required"
            );
        }

        if (tenantId == Guid.Empty)
        {
            return Result<ChannelConnection>.Failure(
                "tenant_id is required",
                "channel.tenant_id_required"
            );
        }

        if (string.IsNullOrWhiteSpace(channelType))
        {
            return Result<ChannelConnection>.Failure(
                "channel_type is required",
                "channel.channel_type_required"
            );
        }

        return Result<ChannelConnection>.Success(
            new ChannelConnection
            {
                ChannelId = channelId,
                TenantId = tenantId,
                ChannelType = channelType.Trim().ToLowerInvariant(),
                SecretEncrypted = secretEncrypted ?? Array.Empty<byte>(),
            }
        );
    }
}
