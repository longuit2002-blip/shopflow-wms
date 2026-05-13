namespace ShopFlow.Mocks.Shopee.Endpoints;

/// <summary>
/// Test-driver payload for <c>POST /__send-webhook</c>. The caller specifies
/// which channel + event to emit; the mock signs the resulting Shopee-shape
/// envelope with the configured secret and POSTs it to the Channel.Api
/// receiver URL.
/// </summary>
public sealed record SendWebhookRequest(
    Guid ChannelId,
    string? EventId,
    string ChannelType,
    string EventType,
    string ExternalOrderId,
    string? ReceiverBaseUrl
);
