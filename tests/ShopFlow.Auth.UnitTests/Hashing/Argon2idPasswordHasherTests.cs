using FluentAssertions;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Infrastructure.Hashing;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Hashing;

/// <summary>
/// Sprint-8 U4 — locks the Argon2id PHC round-trip + the
/// no-throw-on-malformed-input invariant. The
/// <see cref="Argon2idPasswordHasher"/> sits behind
/// <see cref="ShopFlow.Auth.Application.Ports.IPasswordHasher"/>; the
/// login handler at U7 collapses every failure mode here to
/// <c>auth.invalid_credentials</c>, so Verify MUST NOT throw on
/// adversarial or corrupted input.
/// </summary>
/// <remarks>
/// Test parameters are dialled below the OWASP 2026 baseline
/// (4 MiB / 1 iteration / 1 lane) — the algorithm correctness is
/// independent of parameter magnitude and the test suite must run
/// fast. Production parameters live in
/// <see cref="ShopFlow.Auth.Infrastructure.Hashing.Argon2Options"/>
/// defaults (m=64 MiB, t=4, p=4) and are exercised by an
/// integration-tier test that runs in CI.
/// </remarks>
public sealed class Argon2idPasswordHasherTests
{
    private static Argon2idPasswordHasher BuildHasher() =>
        new(Options.Create(new Argon2Options
        {
            MemorySizeKib = 4_096,
            Iterations = 1,
            DegreeOfParallelism = 1,
            HashLengthBytes = 32,
        }));

    [Fact]
    public void Hash_ReturnsPhcModularString()
    {
        var hasher = BuildHasher();

        var phc = hasher.Hash("password123");

        phc.Should().StartWith("$argon2id$v=19$");
        phc.Split('$').Should().HaveCount(6); // leading empty + algo + v + params + salt + hash
    }

    [Fact]
    public void Hash_EncodesConfiguredParameters()
    {
        var hasher = BuildHasher();

        var phc = hasher.Hash("password123");

        phc.Should().Contain("m=4096");
        phc.Should().Contain("t=1");
        phc.Should().Contain("p=1");
    }

    [Fact]
    public void Verify_RoundTrip_ReturnsTrue()
    {
        var hasher = BuildHasher();
        var phc = hasher.Hash("correct horse battery staple");

        hasher.Verify("correct horse battery staple", phc).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = BuildHasher();
        var phc = hasher.Hash("correct horse battery staple");

        hasher.Verify("WRONG horse battery staple", phc).Should().BeFalse();
    }

    [Fact]
    public void Verify_CaseSensitivePassword()
    {
        // Passwords are case-sensitive; ensure verifier does not lowercase.
        var hasher = BuildHasher();
        var phc = hasher.Hash("MixedCasePass");

        hasher.Verify("mixedcasepass", phc).Should().BeFalse();
        hasher.Verify("MixedCasePass", phc).Should().BeTrue();
    }

    [Theory]
    [InlineData("$argon2id$broken-malformed")]
    [InlineData("$argon2id$v=19$$$$")]
    [InlineData("not-a-hash-at-all")]
    [InlineData("")]
    [InlineData("$bcrypt$v=2y$10$something")]
    public void Verify_MalformedHash_ReturnsFalseDoesNotThrow(string corrupted)
    {
        var hasher = BuildHasher();

        var act = () => hasher.Verify("anything", corrupted);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void Verify_FutureAlgorithmName_ReturnsFalse()
    {
        // A hash produced by a hypothetical future Argon2.next algorithm
        // must NOT crash the verifier — it must collapse to invalid.
        var hasher = BuildHasher();
        var phc = "$argon2nx$v=19$m=4096,t=1,p=1$c2FsdA$aGFzaA";

        hasher.Verify("password", phc).Should().BeFalse();
    }

    [Fact]
    public void Hash_TwoCallsSamePlaintext_ProduceDifferentSalts()
    {
        var hasher = BuildHasher();

        var a = hasher.Hash("samepass");
        var b = hasher.Hash("samepass");

        a.Should().NotBe(b, "fresh salt per call");
        // Salt component is the 5th $-separated segment.
        var saltA = a.Split('$')[4];
        var saltB = b.Split('$')[4];
        saltA.Should().NotBe(saltB);
    }

    [Fact]
    public void Verify_RoundTripAcrossDifferentHasherInstances_Succeeds()
    {
        // PHC parameters travel in the hash; an instance with different
        // configured defaults still verifies an existing hash by reading
        // the embedded parameters.
        var hasherA = new Argon2idPasswordHasher(Options.Create(new Argon2Options
        {
            MemorySizeKib = 4_096,
            Iterations = 1,
            DegreeOfParallelism = 1,
            HashLengthBytes = 32,
        }));
        var hasherB = new Argon2idPasswordHasher(Options.Create(new Argon2Options
        {
            // Different defaults — would produce different new hashes,
            // but verify uses the embedded params from hasherA's output.
            MemorySizeKib = 8_192,
            Iterations = 2,
            DegreeOfParallelism = 2,
            HashLengthBytes = 32,
        }));

        var phc = hasherA.Hash("samepass");

        hasherB.Verify("samepass", phc).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Hash_RejectsEmptyPlaintext(string? plaintext)
    {
        var hasher = BuildHasher();

        var act = () => hasher.Hash(plaintext!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Verify_RejectsEmptyPlaintext_AsFalse(string? plaintext)
    {
        // Empty plaintext is a guard at the request DTO level too, but
        // the hasher must collapse the case here instead of throwing
        // so the handler at U7 stays on the invalid_credentials path.
        var hasher = BuildHasher();
        var phc = hasher.Hash("real");

        hasher.Verify(plaintext!, phc).Should().BeFalse();
    }
}
