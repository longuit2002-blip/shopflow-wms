namespace ShopFlow.Mocks.Lazada.Endpoints;

/// <summary>
/// Mutable chaos-injection state (finish-line U7). Singleton; toggled at
/// runtime via <c>POST /__chaos</c>. The <c>/__send-webhook</c> handler
/// reads the rates before deciding whether to inject a 429 / 500 / latency
/// instead of forwarding to the Channel.Api receiver. Mirrors the Shopee
/// mock's <c>ChaosState</c>.
/// </summary>
public sealed class ChaosState
{
    /// <summary>Probability in [0, 1] that the mock returns 429 to the caller.</summary>
    public double Rate429 { get; set; }

    /// <summary>Probability in [0, 1] that the mock returns 500 to the caller.</summary>
    public double Rate500 { get; set; }

    /// <summary>Upper bound on extra latency in milliseconds (uniform [0, max]).</summary>
    public int LatencyJitterMs { get; set; }

    /// <summary>
    /// When set, <c>POST /api/v3/product/update_stock</c> short-circuits to
    /// 503. Lets the integration tests assert the adapter surfaces 5xx as
    /// <c>lazada.push.5xx</c> without touching the per-call probabilistic
    /// rates above.
    /// </summary>
    public bool IsStockUpdateChaosActive { get; set; }
}
