using System.Security.Cryptography;
using System.Text;

namespace ShopFlow.Mocks.Lazada.Signing;

/// <summary>
/// HMAC-SHA256 signer for outgoing webhooks (finish-line U7). Intentionally
/// independent of <c>ShopFlow.Channel.Infrastructure.Signature.LazadaSignatureVerifier</c>
/// (K7) — the mock pretends to be a third-party server, so importing our own
/// code would let mock/production drift go undetected. The function is 8
/// lines; the duplication is cheap and is the drift detector.
/// </summary>
public static class LazadaSigner
{
    public static string Sign(byte[] body, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(secret);
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(body);
        return Convert.ToBase64String(hash);
    }

    public static string Sign(string body, string secret) =>
        Sign(Encoding.UTF8.GetBytes(body), Encoding.UTF8.GetBytes(secret));
}
