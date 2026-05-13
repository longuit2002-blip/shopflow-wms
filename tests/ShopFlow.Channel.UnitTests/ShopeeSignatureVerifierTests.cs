using System.Text;
using ShopFlow.Channel.Infrastructure.Signature;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4 plan U3 — Shopee HMAC-SHA256 verifier coverage. Pins the
/// constant-time compare path and the bad-input → false discipline (no
/// exceptions on garbage).
/// </summary>
public sealed class ShopeeSignatureVerifierTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("shared-secret-bytes");
    private static readonly byte[] OtherSecret = Encoding.UTF8.GetBytes("different-secret");

    private readonly ShopeeSignatureVerifier _verifier = new();

    [Fact]
    public void ChannelType_IsShopee()
    {
        _verifier.ChannelType.Should().Be("shopee");
    }

    [Fact]
    public void Verify_ReturnsTrue_OnValidSignature()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"order.created\",\"event_id\":\"e-1\"}");
        var signature = ShopeeSignatureVerifier.Sign(body, Secret);

        var ok = _verifier.Verify(body, signature, Secret);

        ok.Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnTamperedBody()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"order.created\"}");
        var signature = ShopeeSignatureVerifier.Sign(body, Secret);

        var tampered = Encoding.UTF8.GetBytes("{\"event\":\"order.cancelled\"}");

        _verifier.Verify(tampered, signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnWrongSecret()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"order.created\"}");
        var signature = ShopeeSignatureVerifier.Sign(body, Secret);

        _verifier.Verify(body, signature, OtherSecret).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnEmptySignature()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        _verifier.Verify(body, "", Secret).Should().BeFalse();
        _verifier.Verify(body, "   ", Secret).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnGarbageBase64()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        _verifier.Verify(body, "***-not-base64-***", Secret).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnSignatureWrongLength()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        // Base64 of "abc" decodes to 3 bytes — not the 32 HMACSHA256 expects.
        var tooShort = Convert.ToBase64String(new byte[] { 0x61, 0x62, 0x63 });
        _verifier.Verify(body, tooShort, Secret).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnEmptySecret()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var signature = ShopeeSignatureVerifier.Sign(body, Secret);
        _verifier.Verify(body, signature, ReadOnlySpan<byte>.Empty).Should().BeFalse();
    }

    [Fact]
    public void Verify_HandlesEmptyBody()
    {
        var signature = ShopeeSignatureVerifier.Sign(ReadOnlySpan<byte>.Empty, Secret);
        _verifier.Verify(ReadOnlySpan<byte>.Empty, signature, Secret).Should().BeTrue();
    }

    [Fact]
    public void Sign_StringOverload_MatchesByteOverload()
    {
        var bodyStr = "{\"e\":\"1\"}";
        var secretStr = "shared-secret-bytes";

        var fromBytes = ShopeeSignatureVerifier.Sign(
            Encoding.UTF8.GetBytes(bodyStr),
            Encoding.UTF8.GetBytes(secretStr)
        );
        var fromStrings = ShopeeSignatureVerifier.Sign(bodyStr, secretStr);

        fromStrings.Should().Be(fromBytes);
    }
}

/// <summary>
/// Sprint-4 plan U3 — SignatureVerifierFactory coverage.
/// </summary>
public sealed class SignatureVerifierFactoryTests
{
    [Fact]
    public void Resolve_ReturnsRegisteredVerifier()
    {
        var factory = new SignatureVerifierFactory(new[] { new ShopeeSignatureVerifier() });

        factory.Resolve("shopee").Should().NotBeNull();
        factory.Resolve("Shopee").Should().NotBeNull(); // case-insensitive
    }

    [Fact]
    public void Resolve_ReturnsNullForUnknown()
    {
        var factory = new SignatureVerifierFactory(new[] { new ShopeeSignatureVerifier() });

        factory.Resolve("lazada").Should().BeNull();
    }

    [Fact]
    public void Resolve_ReturnsNullForBlank()
    {
        var factory = new SignatureVerifierFactory(new[] { new ShopeeSignatureVerifier() });

        factory.Resolve(string.Empty).Should().BeNull();
        factory.Resolve("   ").Should().BeNull();
    }
}
