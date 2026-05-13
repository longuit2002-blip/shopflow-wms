using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Domain.Channels;

/// <summary>
/// Tenant-side projection of one marketplace-channel binding (one row in
/// <c>channels</c>) per Sprint-4 plan U1 + Tech Design v3.0 §6. The
/// authoritative <c>channel_connections</c> row lives in the control-plane
/// catalog DB and carries the HMAC secret; this row carries the routing
/// metadata the Channel module's adapters need at runtime
/// (<see cref="ChannelType"/>, <see cref="Status"/>) without a cross-DB read.
/// </summary>
/// <remarks>
/// <para><see cref="BaseEntity.Id"/> mirrors the control-plane
/// <c>ChannelConnection.ChannelId</c> — the receiver routes the inbound
/// webhook via the catalog (<c>IChannelDirectory</c>) and then binds tenant
/// context before any tenant-DB access. Per ADR-0003 the database identity
/// is the tenant boundary so this row carries no <c>tenant_id</c> column.</para>
/// </remarks>
public sealed class Channel : BaseEntity
{
    public string ChannelType { get; private set; } = string.Empty;

    public ChannelStatus Status { get; private set; } = ChannelStatus.Active;

    public DateTime? DisabledAt { get; private set; }

    private Channel() { }

    /// <summary>
    /// Project an Active channel row from a control-plane channel-connection
    /// binding. <paramref name="channelId"/> must equal the control-plane
    /// <c>ChannelConnection.ChannelId</c> so the receiver's directory lookup
    /// resolves to this row.
    /// </summary>
    public static Result<Channel> Create(Guid channelId, string channelType)
    {
        if (channelId == Guid.Empty)
        {
            return Result<Channel>.Failure(
                "channel_id is required.",
                "channel.channel_id_required"
            );
        }
        if (string.IsNullOrWhiteSpace(channelType))
        {
            return Result<Channel>.Failure(
                "channel_type is required.",
                "channel.channel_type_required"
            );
        }
        var normalized = channelType.Trim().ToLowerInvariant();
        if (normalized.Length > 32)
        {
            return Result<Channel>.Failure(
                "channel_type must be 32 characters or fewer.",
                "channel.channel_type_too_long"
            );
        }

        return Result<Channel>.Success(
            new Channel
            {
                Id = channelId,
                ChannelType = normalized,
                Status = ChannelStatus.Active,
            }
        );
    }

    /// <summary>
    /// Active → Disabled. Idempotent: a second call on already-Disabled is
    /// a no-op success (admin retries are safe).
    /// </summary>
    public Result Disable(DateTime now)
    {
        if (Status == ChannelStatus.Disabled)
        {
            return Result.Success();
        }
        Status = ChannelStatus.Disabled;
        DisabledAt = now;
        UpdatedAt = now;
        return Result.Success();
    }
}
