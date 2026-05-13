namespace ShopFlow.Mocks.Shopee.Endpoints;

/// <summary>
/// Mutable chaos-injection state per Sprint-4 plan U7. Singleton; toggled
/// at runtime via <c>POST /__chaos</c>. <see cref="WebhookSender"/> reads
/// the rates before deciding whether to inject a 429 / 500 / latency
/// instead of forwarding to the Channel.Api receiver.
/// </summary>
public sealed class ChaosState
{
    /// <summary>Probability in [0, 1] that the mock returns 429 to the caller.</summary>
    public double Rate429 { get; set; }

    /// <summary>Probability in [0, 1] that the mock returns 500 to the caller.</summary>
    public double Rate500 { get; set; }

    /// <summary>Upper bound on extra latency in milliseconds (uniform [0, max]).</summary>
    public int LatencyJitterMs { get; set; }
}
