using FluentAssertions;
using ShopFlow.Auth.Application.Services;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Services;

/// <summary>
/// Sprint-8 U8 — generator-output discipline. The temporary
/// password generator is the only place the system produces
/// credentials on behalf of users, so its output must be (1)
/// long enough to brute-force-resist, (2) sampled from a vetted
/// alphabet, and (3) mix categories so a downstream-policy
/// "must contain X" check doesn't reject the very password the
/// system itself just minted.
/// </summary>
public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generate_ReturnsExpectedLength()
    {
        var gen = new PasswordGenerator();

        var pwd = gen.Generate();

        pwd.Length.Should().Be(PasswordGenerator.Length);
    }

    [Fact]
    public void Generate_HundredCallsProduceDistinctPasswords()
    {
        var gen = new PasswordGenerator();
        var seen = new HashSet<string>();

        for (var i = 0; i < 100; i++)
        {
            seen.Add(gen.Generate());
        }

        seen.Count.Should().Be(100, "RNG must produce distinct outputs");
    }

    [Fact]
    public void Generate_DoesNotContainVisuallyAmbiguousCharacters()
    {
        var gen = new PasswordGenerator();
        var ambiguous = new[] { '0', 'O', 'o', '1', 'l', 'I' };

        for (var i = 0; i < 50; i++)
        {
            var pwd = gen.Generate();
            pwd.Should()
                .NotContainAny(
                    ambiguous.Select(c => c.ToString()),
                    because: "alphabet excludes 0/O/o/1/l/I per spec"
                );
        }
    }

    [Fact]
    public void Generate_ContainsAtLeastOneFromEachOfTheFourCategories()
    {
        var gen = new PasswordGenerator();
        var lower = "abcdefghijkmnpqrstuvwxyz";
        var upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        var digits = "23456789";
        var symbols = "!@#$%^&*+-_";

        for (var i = 0; i < 25; i++)
        {
            var pwd = gen.Generate();
            pwd.Any(c => lower.Contains(c)).Should().BeTrue("contains lowercase");
            pwd.Any(c => upper.Contains(c)).Should().BeTrue("contains uppercase");
            pwd.Any(c => digits.Contains(c)).Should().BeTrue("contains digit");
            pwd.Any(c => symbols.Contains(c)).Should().BeTrue("contains symbol");
        }
    }

    [Fact]
    public void Generate_OutputPassesAuthMinLengthGate()
    {
        var gen = new PasswordGenerator();

        var pwd = gen.Generate();

        pwd.Length.Should()
            .BeGreaterThanOrEqualTo(
                8,
                because: "ChangePassword's auth.password_too_short floor is 8"
            );
    }
}
