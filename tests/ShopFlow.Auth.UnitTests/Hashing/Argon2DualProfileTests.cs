using FluentAssertions;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Infrastructure.Hashing;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Hashing;

/// <summary>
/// Sprint-9 U3 — pin the Argon2 dual-profile contract. Password hashes
/// must use OWASP 2026 params; RecoveryCode hashes use the lighter
/// profile (KTD9). Verify is profile-blind because the PHC string
/// parameter-embeds the params.
/// </summary>
public sealed class Argon2DualProfileTests
{
    private static Argon2idPasswordHasher BuildHasher() => new(Options.Create(new Argon2Options()));

    [Fact]
    public void Hash_Password_EmbedsOwaspBaselineParams()
    {
        var hasher = BuildHasher();
        var hash = hasher.Hash("Sup3rSecret!Pass", Argon2Profile.Password);

        hash.Should().StartWith("$argon2id$v=19$m=65536,t=4,p=4$");
    }

    [Fact]
    public void Hash_RecoveryCode_EmbedsLighterProfileParams()
    {
        var hasher = BuildHasher();
        var hash = hasher.Hash("RECOV-1234", Argon2Profile.RecoveryCode);

        hash.Should().StartWith("$argon2id$v=19$m=8192,t=2,p=1$");
    }

    [Fact]
    public void Hash_DefaultProfileIsPassword()
    {
        var hasher = BuildHasher();
        var hash = hasher.Hash("DefaultProfileTest");

        hash.Should().StartWith("$argon2id$v=19$m=65536,t=4,p=4$");
    }

    [Theory]
    [InlineData(Argon2Profile.Password)]
    [InlineData(Argon2Profile.RecoveryCode)]
    public void Verify_RoundTripsRegardlessOfProfile(Argon2Profile profile)
    {
        var hasher = BuildHasher();
        const string plaintext = "RoundTripPlaintext-ABC";
        var hash = hasher.Hash(plaintext, profile);

        hasher.Verify(plaintext, hash).Should().BeTrue();
        hasher.Verify("WrongPlaintext", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_PasswordHash_AgainstRecoveryProfileCall_StillWorks()
    {
        // The PHC string carries its own params — Verify reads them and
        // doesn't care which Argon2Profile produced the hash.
        var hasher = BuildHasher();
        var passwordHash = hasher.Hash("Same-Plaintext", Argon2Profile.Password);
        var recoveryHash = hasher.Hash("Same-Plaintext", Argon2Profile.RecoveryCode);

        hasher.Verify("Same-Plaintext", passwordHash).Should().BeTrue();
        hasher.Verify("Same-Plaintext", recoveryHash).Should().BeTrue();
        // The two hashes differ — different salt + different work factor.
        passwordHash.Should().NotBe(recoveryHash);
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalseNotThrow()
    {
        var hasher = BuildHasher();
        hasher.Verify("anything", "not-a-phc-string").Should().BeFalse();
        hasher.Verify("anything", "").Should().BeFalse();
        hasher.Verify("anything", "$argon2id$v=19$").Should().BeFalse();
    }
}
