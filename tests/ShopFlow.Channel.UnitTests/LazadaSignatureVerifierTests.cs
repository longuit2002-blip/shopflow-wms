using System.Text;
using ShopFlow.Channel.Infrastructure.Signature;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Finish-line U7 — Lazada HMAC-SHA256 verifier coverage. Mirrors
/// <see cref="ShopeeSignatureVerifierTests"/>: pins the constant-time
/// compare path, the bad-input → false discipline, and the channel-agnostic
/// signature header name (K8).
/// </summary>
public sealed class LazadaSignatureVerifierTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("lazada-shared-secret");
    private static readonly byte[] OtherSecret = Encoding.UTF8.GetBytes("different-secret");

    private readonly LazadaSignatureVerifier _verifier = new();

    [Fact]
    public void ChannelType_IsLazada()
    {
        _verifier.ChannelType.Should().Be("lazada");
    }

    [Fact]
    public void SignatureHeaderName_IsXLazadaSignature()
    {
        _verifier.SignatureHeaderName.Should().Be("X-Lazada-Signature");
    }

    [Fact]
    public void Verify_ReturnsTrue_OnValidSignature_RoundTrip()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"order.created\",\"event_id\":\"e-1\"}");
        var signature = LazadaSignatureVerifier.Sign(body, Secret);

        var ok = _verifier.Verify(body, signature, Secret);

        ok.Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnTamperedBody()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"order.created\"}");
        var signature = LazadaSignatureVerifier.Sign(body, Secret);

        var tampered = Encoding.UTF8.GetBytes("{\"event\":\"order.cancelled\"}");

        _verifier.Verify(tampered, signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_OnWrongSecret()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"order.created\"}");
        var signature = LazadaSignatureVerifier.Sign(body, Secret);

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
        var signature = LazadaSignatureVerifier.Sign(body, Secret);
        _verifier.Verify(body, signature, ReadOnlySpan<byte>.Empty).Should().BeFalse();
    }

    [Fact]
    public void Sign_StringOverload_MatchesByteOverload()
    {
        var bodyStr = "{\"e\":\"1\"}";
        var secretStr = "lazada-shared-secret";

        var fromBytes = LazadaSignatureVerifier.Sign(
            Encoding.UTF8.GetBytes(bodyStr),
            Encoding.UTF8.GetBytes(secretStr)
        );
        var fromStrings = LazadaSignatureVerifier.Sign(bodyStr, secretStr);

        fromStrings.Should().Be(fromBytes);
    }
}
