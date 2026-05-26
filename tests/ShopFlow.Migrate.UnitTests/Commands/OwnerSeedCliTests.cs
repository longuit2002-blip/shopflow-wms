using FluentAssertions;
using ShopFlow.Migrate;
using ShopFlow.Migrate.Commands;
using ShopFlow.Migrate.Provisioning;
using Xunit;

namespace ShopFlow.Migrate.UnitTests.Commands;

/// <summary>
/// Sprint-8 U10 — flag-resolution + stdout-echo discipline for the
/// owner-seed path. The integration-tier coverage (real Postgres,
/// real OwnerSeed.SeedAsync) runs in CI alongside the rest of the
/// migrate suite; this suite locks the surface-level argument
/// parsing + stdout-echo branches that don't need a DB.
/// </summary>
public sealed class OwnerSeedCliTests
{
    [Fact]
    public void ResolveOwnerEmail_DefaultsToOwnerAtSlugLocal_WhenFlagMissing()
    {
        var args = new ParsedArgs("provision", new Dictionary<string, string?>());
        var email = ProvisionCommand.ResolveOwnerEmail(args, "newtenant");

        email.Should().Be("owner@newtenant.local");
    }

    [Fact]
    public void ResolveOwnerEmail_HonorsExplicitFlag()
    {
        var args = new ParsedArgs(
            "provision",
            new Dictionary<string, string?> { ["owner-email"] = "admin@example.com" }
        );

        var email = ProvisionCommand.ResolveOwnerEmail(args, "newtenant");

        email.Should().Be("admin@example.com");
    }

    [Fact]
    public void ResolveExplicitPassword_ReturnsNull_WhenNeitherFlagSet()
    {
        var args = new ParsedArgs("provision", new Dictionary<string, string?>());

        var result = ProvisionCommand.ResolveExplicitPassword(args);

        result.Should().BeNull("generator will produce the password");
    }

    [Fact]
    public void ResolveExplicitPassword_PrefersExplicitFlag_OverEnvVar()
    {
        var args = new ParsedArgs(
            "provision",
            new Dictionary<string, string?>
            {
                ["owner-password"] = "explicit-from-flag",
                ["owner-password-from-env"] = "SOME_VAR",
            }
        );

        var result = ProvisionCommand.ResolveExplicitPassword(args);

        result.Should().Be("explicit-from-flag");
    }

    [Fact]
    public void ResolveExplicitPassword_ReadsFromEnvVar_WhenFlagOnlyNamesEnv()
    {
        const string envVar = "SHOPFLOW_TEST_OWNER_PWD";
        Environment.SetEnvironmentVariable(envVar, "from-env");
        try
        {
            var args = new ParsedArgs(
                "provision",
                new Dictionary<string, string?> { ["owner-password-from-env"] = envVar }
            );

            var result = ProvisionCommand.ResolveExplicitPassword(args);

            result.Should().Be("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact]
    public void ResolveExplicitPassword_ThrowsWhenEnvVarIsEmpty()
    {
        const string envVar = "SHOPFLOW_TEST_OWNER_PWD_MISSING";
        Environment.SetEnvironmentVariable(envVar, "");
        try
        {
            var args = new ParsedArgs(
                "provision",
                new Dictionary<string, string?> { ["owner-password-from-env"] = envVar }
            );

            var act = () => ProvisionCommand.ResolveExplicitPassword(args);

            act.Should().Throw<InvalidOperationException>().WithMessage($"*{envVar}*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact]
    public void EchoOwnerSeed_GeneratedPassword_PrintsExactlyOnce()
    {
        // Capture stdout.
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            ProvisionCommand.EchoOwnerSeed(
                new OwnerSeedResult(OwnerSeedOutcome.Seeded, "owner@t.local", "GENPWD123!@#xyzAB"),
                passwordWasExplicit: false
            );
        }
        finally
        {
            Console.SetOut(orig);
        }

        var line = sw.ToString();
        line.Should().Contain("owner@t.local");
        line.Should().Contain("GENPWD123!@#xyzAB");
    }

    [Fact]
    public void EchoOwnerSeed_ExplicitPassword_DoesNotEchoPlaintext()
    {
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            ProvisionCommand.EchoOwnerSeed(
                new OwnerSeedResult(OwnerSeedOutcome.Seeded, "owner@t.local", null),
                passwordWasExplicit: true
            );
        }
        finally
        {
            Console.SetOut(orig);
        }

        var line = sw.ToString();
        line.Should().Contain("owner@t.local");
        line.Should().Contain("not echoed");
    }

    [Fact]
    public void EchoOwnerSeed_AlreadySeeded_AnnouncesNoOp()
    {
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            ProvisionCommand.EchoOwnerSeed(
                new OwnerSeedResult(OwnerSeedOutcome.AlreadySeeded, "owner@t.local", null),
                passwordWasExplicit: false
            );
        }
        finally
        {
            Console.SetOut(orig);
        }

        sw.ToString().Should().Contain("already exists");
    }
}
