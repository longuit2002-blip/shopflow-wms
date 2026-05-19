using FluentAssertions;
using ShopFlow.Inventory.Infrastructure.Pagination;
using Xunit;

namespace ShopFlow.Inventory.UnitTests.Pagination;

/// <summary>
/// Sprint-7.5 U6 — pins the opaque-base64-cursor round-trip + invalid
/// input handling. Controller maps a null TryDecode to a 400; handler
/// trusts the payload from there.
/// </summary>
public sealed class OpaqueCursorTests
{
    [Fact]
    public void Encode_Decode_RoundTrip_PreservesPayload()
    {
        var original = new OpaqueCursorPayload(
            OccurredAt: new DateTime(2026, 5, 18, 14, 32, 17, 14, DateTimeKind.Utc),
            Id: Guid.Parse("11111111-2222-3333-4444-555555555555"));

        var encoded = OpaqueCursor.Encode(original);
        var decoded = OpaqueCursor.TryDecode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Id.Should().Be(original.Id);
        decoded.OccurredAt.Should().Be(original.OccurredAt);
    }

    [Fact]
    public void Encode_ProducesUrlSafeBase64()
    {
        var payload = new OpaqueCursorPayload(DateTime.UtcNow, Guid.NewGuid());

        var encoded = OpaqueCursor.Encode(payload);

        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
        encoded.Should().NotContain("=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryDecode_NullOrEmpty_ReturnsNull(string? cursor)
    {
        OpaqueCursor.TryDecode(cursor).Should().BeNull();
    }

    [Fact]
    public void TryDecode_InvalidBase64_ReturnsNull()
    {
        OpaqueCursor.TryDecode("not-valid-base64!@#$%").Should().BeNull();
    }

    [Fact]
    public void TryDecode_ValidBase64_InvalidJson_ReturnsNull()
    {
        // "not-json" → "bm90LWpzb24" in url-safe base64
        OpaqueCursor.TryDecode("bm90LWpzb24").Should().BeNull();
    }

    [Fact]
    public void TryDecode_EmptyGuid_ReturnsNull()
    {
        // Empty id is treated as malformed — defensive against
        // partially-constructed payloads.
        var bad = new OpaqueCursorPayload(DateTime.UtcNow, Guid.Empty);
        var encoded = OpaqueCursor.Encode(bad);

        OpaqueCursor.TryDecode(encoded).Should().BeNull();
    }
}
