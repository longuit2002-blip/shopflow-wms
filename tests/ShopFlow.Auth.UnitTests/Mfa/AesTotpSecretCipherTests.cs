using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Infrastructure.Mfa;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Mfa;

/// <summary>
/// Sprint-9 U4 — AES-256-GCM envelope cipher contract. Round-trips,
/// AAD binding, KEK rotation fallback, and tamper detection are all
/// load-bearing for the TOTP-secret-at-rest invariant.
/// </summary>
public sealed class AesTotpSecretCipherTests
{
    private static readonly byte[] CurrentKey = RandomBytes(32);
    private static readonly byte[] PreviousKey = RandomBytes(32);

    private static byte[] RandomBytes(int n) => RandomNumberGenerator.GetBytes(n);

    private static AesTotpSecretCipher BuildCipher(int currentKeyId = 2, bool hasPrevious = true)
    {
        var opts = new TotpKekOptions
        {
            Current = Convert.ToBase64String(CurrentKey),
            Previous = hasPrevious ? Convert.ToBase64String(PreviousKey) : null,
            CurrentKeyId = currentKeyId,
        };
        return new AesTotpSecretCipher(Options.Create(opts));
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTripsUnderSameKey()
    {
        var cipher = BuildCipher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var plaintext = RandomBytes(20);

        var (blob, keyId) = cipher.Encrypt(plaintext, tenantId, userId);
        keyId.Should().Be(cipher.CurrentKeyId);
        blob.Length.Should().Be(20 + 12 + 16, "framed blob = nonce(12) + cipher(N) + tag(16)");

        var recovered = cipher.Decrypt(blob, keyId, tenantId, userId);

        recovered.Should().Equal(plaintext);
    }

    [Fact]
    public void Decrypt_WithPreviousKeyId_FallsBackThroughRotationSlot()
    {
        // Encrypt under "Previous" key by swapping which key is Current
        // (CurrentKeyId=1, but Current option holds the PreviousKey
        // bytes so the blob is decryptable later when we flip).
        var legacyOpts = new TotpKekOptions
        {
            Current = Convert.ToBase64String(PreviousKey),
            Previous = null,
            CurrentKeyId = 1,
        };
        var legacy = new AesTotpSecretCipher(Options.Create(legacyOpts));
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (legacyBlob, legacyKeyId) = legacy.Encrypt(RandomBytes(20), tenantId, userId);

        // Now rotate — Current is the new key, Previous is the legacy.
        var rotated = BuildCipher();
        var recovered = rotated.Decrypt(legacyBlob, legacyKeyId, tenantId, userId);

        recovered.Should().NotBeNull();
        recovered!.Length.Should().Be(20);
    }

    [Fact]
    public void Decrypt_WithUnknownKeyId_ReturnsNull()
    {
        var cipher = BuildCipher(currentKeyId: 2, hasPrevious: false);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (blob, _) = cipher.Encrypt(RandomBytes(20), tenantId, userId);

        var recovered = cipher.Decrypt(blob, keyId: 99, tenantId, userId);

        recovered.Should().BeNull("unknown key id without a Previous slot is unrecoverable");
    }

    [Fact]
    public void Decrypt_WithDifferentAad_ThrowsTagMismatch()
    {
        var cipher = BuildCipher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (blob, keyId) = cipher.Encrypt(RandomBytes(20), tenantId, userId);

        var otherTenantId = Guid.NewGuid();
        var act = () => cipher.Decrypt(blob, keyId, otherTenantId, userId);

        act.Should().Throw<AuthenticationTagMismatchException>(
            "AAD binds the ciphertext to (tenantId, userId) — different tenant fails");
    }

    [Fact]
    public void Decrypt_TamperedBlob_ThrowsTagMismatch()
    {
        var cipher = BuildCipher();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (blob, keyId) = cipher.Encrypt(RandomBytes(20), tenantId, userId);

        // Flip the middle ciphertext byte
        blob[20] ^= 0xFF;

        var act = () => cipher.Decrypt(blob, keyId, tenantId, userId);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Decrypt_ShortBlob_ReturnsNull()
    {
        var cipher = BuildCipher();
        var recovered = cipher.Decrypt(new byte[20], 1, Guid.NewGuid(), Guid.NewGuid());

        recovered.Should().BeNull("blob too short to even hold nonce+tag");
    }

    [Fact]
    public void Constructor_RejectsMissingCurrentKey()
    {
        var act = () => new AesTotpSecretCipher(Options.Create(new TotpKekOptions { Current = "" }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Current*");
    }

    [Fact]
    public void Constructor_RejectsWrongLengthKey()
    {
        var opts = new TotpKekOptions { Current = Convert.ToBase64String(RandomBytes(16)) };
        var act = () => new AesTotpSecretCipher(Options.Create(opts));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }
}
