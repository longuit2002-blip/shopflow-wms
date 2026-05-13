using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Application-layer port over the (mocked) external shipping carrier per
/// Sprint-3-redux U6. Sprint-3-redux ships a single Mock implementation
/// in <c>ShopFlow.Outbound.Infrastructure.Shipping</c> with a Polly v8
/// <c>ResiliencePipelineBuilder</c> retry pipeline keyed on
/// <c>TransientShippingException</c>; Phase-2 wires per-channel real
/// carrier adapters (J&amp;T, Ninja Van, etc.) behind the same port shape.
/// </summary>
/// <remarks>
/// <para>The port returns a <see cref="ShippingLabel"/> value tuple
/// (label URL + tracking number) on success; on terminal failure (Polly
/// retries exhausted) the implementation throws — the
/// <c>POST /confirm-ship</c> endpoint catches and maps to
/// <c>503 ProblemDetails</c> with code <c>shipping.carrier_unavailable</c>.</para>
///
/// <para>No tenant context required at the port level — the carrier call
/// is a pure HTTP-shaped operation; tenant context is already bound on
/// the controller (via <c>IRequestContext</c>) and the persisted order
/// row carries the only tenant-scoped identity the call needs.</para>
/// </remarks>
public interface IMockShippingProvider
{
    /// <summary>
    /// Create a shipping label for <paramref name="order"/>. On success,
    /// returns the label URL + tracking number to be persisted on the
    /// order row. On terminal failure (Polly retries exhausted), throws
    /// <c>TransientShippingException</c> — caller maps to 503.
    /// </summary>
    Task<ShippingLabel> CreateLabelAsync(Order order, CancellationToken ct);
}
