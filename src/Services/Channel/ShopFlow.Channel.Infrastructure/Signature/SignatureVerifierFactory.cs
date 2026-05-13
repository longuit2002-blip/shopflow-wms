using ShopFlow.Channel.Application.Ports;

namespace ShopFlow.Channel.Infrastructure.Signature;

/// <summary>
/// Resolves <see cref="ISignatureVerifier"/> instances by channel type per
/// Sprint-4 plan U3 + U5. Implementations are registered as keyed services
/// in <c>AddChannelModule</c>; Sprint-6 adds Lazada by appending one DI
/// line + a Lazada verifier — zero changes to this resolver shape.
/// </summary>
public sealed class SignatureVerifierFactory : ISignatureVerifierFactory
{
    private readonly IReadOnlyDictionary<string, ISignatureVerifier> _verifiers;

    public SignatureVerifierFactory(IEnumerable<ISignatureVerifier> verifiers)
    {
        ArgumentNullException.ThrowIfNull(verifiers);
        _verifiers = verifiers.ToDictionary(
            v => v.ChannelType,
            v => v,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public ISignatureVerifier? Resolve(string channelType)
    {
        if (string.IsNullOrWhiteSpace(channelType))
        {
            return null;
        }
        return _verifiers.TryGetValue(channelType, out var verifier) ? verifier : null;
    }
}
