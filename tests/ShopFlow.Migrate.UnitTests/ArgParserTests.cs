using ShopFlow.Migrate;

namespace ShopFlow.Migrate.UnitTests;

public class ArgParserTests
{
    [Fact]
    public void Parse_no_args_returns_help()
    {
        var result = ArgParser.Parse(Array.Empty<string>());

        result.ShowHelp.Should().BeTrue();
        result.IsOk.Should().BeFalse();
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Parse_help_token_returns_help(string token)
    {
        var result = ArgParser.Parse(new[] { token });

        result.ShowHelp.Should().BeTrue();
    }

    [Fact]
    public void Parse_unknown_subcommand_is_error()
    {
        var result = ArgParser.Parse(new[] { "explode" });

        result.IsOk.Should().BeFalse();
        result.ErrorMessage.Should().Contain("unknown subcommand 'explode'");
    }

    [Fact]
    public void Parse_provision_catalog_succeeds()
    {
        var result = ArgParser.Parse(new[] { "provision", "--catalog" });

        result.IsOk.Should().BeTrue();
        result.Args!.Subcommand.Should().Be("provision");
        result.Args.HasFlag("catalog").Should().BeTrue();
        result.Args.GetFlag("catalog").Should().BeNull();
    }

    [Fact]
    public void Parse_provision_tenant_equals_form_succeeds()
    {
        var result = ArgParser.Parse(new[] { "provision", "--tenant=acme" });

        result.IsOk.Should().BeTrue();
        result.Args!.GetFlag("tenant").Should().Be("acme");
    }

    [Fact]
    public void Parse_provision_tenant_space_form_succeeds()
    {
        var result = ArgParser.Parse(new[] { "provision", "--tenant", "acme" });

        result.IsOk.Should().BeTrue();
        result.Args!.GetFlag("tenant").Should().Be("acme");
    }

    [Fact]
    public void Parse_apply_with_target_and_concurrency_succeeds()
    {
        var result = ArgParser.Parse(
            new[] { "apply", "--target=20260512000000", "--concurrency=8" }
        );

        result.IsOk.Should().BeTrue();
        result.Args!.GetFlag("target").Should().Be("20260512000000");
        result.Args.GetFlag("concurrency").Should().Be("8");
    }

    [Fact]
    public void Parse_status_takes_no_flags()
    {
        var result = ArgParser.Parse(new[] { "status" });

        result.IsOk.Should().BeTrue();
        result.Args!.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_duplicate_flag_is_error()
    {
        var result = ArgParser.Parse(
            new[] { "provision", "--tenant=a", "--tenant=b" }
        );

        result.IsOk.Should().BeFalse();
        result.ErrorMessage.Should().Contain("duplicate flag '--tenant'");
    }

    [Fact]
    public void Parse_positional_after_subcommand_is_error()
    {
        var result = ArgParser.Parse(new[] { "provision", "bogus" });

        result.IsOk.Should().BeFalse();
        result.ErrorMessage.Should().Contain("expected flag");
    }

    [Fact]
    public void RequireFlag_throws_for_missing_flag()
    {
        var args = new ParsedArgs(
            "provision",
            new Dictionary<string, string?> { ["catalog"] = null }
        );

        var act = () => args.RequireFlag("tenant");

        act.Should().Throw<InvalidOperationException>().WithMessage("*--tenant*");
    }
}
