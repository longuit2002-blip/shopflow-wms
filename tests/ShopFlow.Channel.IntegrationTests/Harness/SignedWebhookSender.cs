using System.Security.Cryptography;
using System.Text;

namespace ShopFlow.Channel.IntegrationTests.Harness;

/// <summary>
/// HMAC-SHA256 signer matching <c>ShopeeSigner</c> in
/// <c>tools/mocks/shopee/Signing/ShopeeSigner.cs</c>. Sprint-4.5 U4 plan
/// notes: "keep the harness-side signer in sync (or share the
/// implementation)." Following the same precedent as the mock — the
/// function is small enough that duplication catches drift, importing
/// either implementation directly would hide it.
/// </summary>
internal static class SignedWebhookSender
{
    public static string Sign(byte[] body, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(secret);
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(body);
        return Convert.ToBase64String(hash);
    }

    public static string Sign(string body, byte[] secret) =>
        Sign(Encoding.UTF8.GetBytes(body), secret);
}
