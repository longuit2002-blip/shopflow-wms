using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Outbound;

namespace ShopFlow.Outbound.Infrastructure.Consumers;

/// <summary>
/// Stub consumer for <see cref="TrackingPushedV1"/> per Sprint-3-redux K9.
/// Logs the event and ACKs. Phase-2 Sprint-4 moves this to
/// <c>ShopFlow.Channel.Infrastructure/Consumers/</c> where the real
/// channel adapter will push the tracking info back to the marketplace
/// (Shopee/Lazada/TikTok/Shopify) via the per-tenant channel-connection
/// table.
/// </summary>
/// <remarks>
/// Auto-registered via <c>AddConsumers(asm)</c> in the kernel-wide
/// <c>AddShopFlowDefaults</c> MassTransit configuration — the Outbound
/// Infrastructure assembly is scanned and every <see cref="IConsumer{T}"/>
/// is picked up. No explicit registration in
/// <see cref="OutboundServiceCollectionExtensions"/>.
/// </remarks>
public sealed class ChannelTrackingConsumer : IConsumer<TrackingPushedV1>
{
    private readonly ILogger<ChannelTrackingConsumer> _logger;

    public ChannelTrackingConsumer(ILogger<ChannelTrackingConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TrackingPushedV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        _logger.LogInformation(
            "Tracking pushed: order={OrderId} tenant={TenantId} tracking={TrackingNumber} label={LabelUrl}",
            msg.OrderId,
            msg.TenantId,
            msg.TrackingNumber,
            msg.LabelUrl
        );
        return Task.CompletedTask;
    }
}
