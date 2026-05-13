namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Thrown by <see cref="IMockShippingProvider.CreateLabelAsync"/> on a
/// transient carrier failure (Sprint-3-redux U6). The Polly v8
/// <c>ResiliencePipelineBuilder</c> retry pipeline keys
/// <c>ShouldHandle</c> on this exception type — only thrown errors of
/// this shape are retried; anything else propagates immediately.
/// </summary>
/// <remarks>
/// Lives in Application (not Infrastructure) so the consuming code path
/// (controller) can catch + map without taking an Infrastructure
/// reference. Mirrors Polly's documented "exception-type-keyed retry"
/// pattern.
/// </remarks>
public sealed class TransientShippingException : Exception
{
    public TransientShippingException()
        : base("Mock carrier transient failure.") { }

    public TransientShippingException(string message)
        : base(message) { }

    public TransientShippingException(string message, Exception inner)
        : base(message, inner) { }
}
