namespace ShopFlow.Channel.Application.Ports;

/// <summary>
/// Per-channel-type signature verification port per Sprint-4 plan U3.
/// Receivers extract the raw request body + provider signature header,
/// look up the per-tenant secret via <c>IChannelDirectory</c>, then call
/// this port to validate. Implementations live in
/// <c>ShopFlow.Channel.Infrastructure.Signature</c> and are resolved per
/// channel type via DI (Sprint-6 adds a Lazada impl alongside Shopee).
/// </summary>
/// <remarks>
/// <para>Constant-time comparison is mandatory:
/// <c>CryptographicOperations.FixedTimeEquals</c> in the implementation.
/// Timing side channels on signature comparison are a real attack on
/// marketplace webhook receivers — the Sprint-4 plan's "HMAC verification
/// timing-attack hole" risk row.</para>
/// </remarks>
public interface ISignatureVerifier
{
    /// <summary>
    /// The channel type this verifier handles (lower-case, e.g. <c>"shopee"</c>).
    /// </summary>
    string ChannelType { get; }

    /// <summary>
    /// Validate <paramref name="signature"/> against the HMAC of
    /// <paramref name="body"/> keyed by <paramref name="secret"/>. Returns
    /// false on any mismatch, missing input, or malformed signature.
    /// Never throws on bad input — the caller treats false as 401.
    /// </summary>
    bool Verify(ReadOnlySpan<byte> body, string signature, ReadOnlySpan<byte> secret);
}

/// <summary>
/// Factory port resolving <see cref="ISignatureVerifier"/> by channel type.
/// </summary>
public interface ISignatureVerifierFactory
{
    /// <summary>
    /// Resolve the verifier for <paramref name="channelType"/>. Returns null
    /// when no verifier is registered — the receiver returns 501 to surface
    /// the missing adapter loudly during Sprint-6+ rollouts.
    /// </summary>
    ISignatureVerifier? Resolve(string channelType);
}
