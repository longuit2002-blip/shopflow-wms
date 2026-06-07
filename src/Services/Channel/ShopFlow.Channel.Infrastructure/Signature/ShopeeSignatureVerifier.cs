using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using ShopFlow.Channel.Application.Ports;

namespace ShopFlow.Channel.Infrastructure.Signature;

/// <summary>
/// HMAC-SHA256 verifier for Shopee-shape webhook signatures per Sprint-4
/// plan U3. The provider sends a base64-encoded HMAC of the raw request
/// body keyed by the channel's secret; we recompute on our side and
/// compare in constant time via
/// <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </summary>
/// <remarks>
/// <para>Constant-time compare is mandatory — a regular byte/string equals
/// leaks information about the prefix of the computed HMAC and lets a
/// remote attacker recover bytes one at a time. <c>FixedTimeEquals</c> is
/// the .NET BCL primitive built for this exact threat.</para>
/// <para>The verifier never throws on bad input — receiver-side defense is
/// "return false → 401". Empty / whitespace signature, base64 decode
/// failure, length mismatch all reduce to false.</para>
/// </remarks>
public sealed class ShopeeSignatureVerifier : ISignatureVerifier
{
    public string ChannelType => "shopee";

    /// <summary>
    /// Shopee sends its HMAC base64 signature in <c>X-Shopee-Signature</c>.
    /// The receiver reads this header per channel type (finish-line K8).
    /// </summary>
    public string SignatureHeaderName => "X-Shopee-Signature";

    public bool Verify(ReadOnlySpan<byte> body, string signature, ReadOnlySpan<byte> secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || secret.IsEmpty)
        {
            return false;
        }

        // Decode the provider-supplied signature. Shopee sends base64; if
        // decoding fails treat as invalid rather than throwing.
        var providedBytes = ArrayPool<byte>.Shared.Rent(64);
        try
        {
            if (!Convert.TryFromBase64String(signature, providedBytes, out var providedLen))
            {
                return false;
            }

            // HMACSHA256 always produces 32 bytes; mismatching length means
            // the provider sent something we don't expect.
            if (providedLen != HMACSHA256.HashSizeInBytes)
            {
                return false;
            }

            Span<byte> computed = stackalloc byte[HMACSHA256.HashSizeInBytes];
            if (!HMACSHA256.TryHashData(secret, body, computed, out var written))
            {
                return false;
            }
            if (written != HMACSHA256.HashSizeInBytes)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                providedBytes.AsSpan(0, providedLen),
                computed
            );
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(providedBytes, clearArray: true);
        }
    }

    /// <summary>
    /// Test helper — produces the canonical signature for a given (body,
    /// secret) pair. Mirrors the mock server's signing primitive (U7) so
    /// integration tests can sign locally without spinning the mock.
    /// </summary>
    public static string Sign(ReadOnlySpan<byte> body, ReadOnlySpan<byte> secret)
    {
        Span<byte> computed = stackalloc byte[HMACSHA256.HashSizeInBytes];
        var ok = HMACSHA256.TryHashData(secret, body, computed, out _);
        if (!ok)
        {
            throw new InvalidOperationException("HMAC computation failed.");
        }
        return Convert.ToBase64String(computed);
    }

    /// <summary>
    /// Convenience overload for tests that prefer UTF-8 strings.
    /// </summary>
    public static string Sign(string body, string secret) =>
        Sign(Encoding.UTF8.GetBytes(body), Encoding.UTF8.GetBytes(secret));
}
