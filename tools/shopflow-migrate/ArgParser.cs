namespace ShopFlow.Migrate;

/// <summary>
/// Hand-rolled argument parser for <c>shopflow-migrate</c>. No
/// System.CommandLine dep — the subcommand grammar is small enough that a
/// dictionary of switches beats a package version bump (shopflow-gate
/// precedent: <c>a5c1cb6 feat(gate): ...</c>).
/// </summary>
/// <remarks>
/// <para>Grammar (BNF-ish):</para>
/// <code>
/// args         := subcommand flag*
/// subcommand   := "provision" | "apply" | "archive" | "restore" | "status" | "help" | "--help" | "-h"
/// flag         := "--name"           // bare switch
///               | "--name=value"     // long form with value
///               | "--name" value     // long form with space-separated value
/// </code>
/// <para>Flag names are case-sensitive; values are not coerced (callers
/// validate types). Unknown flags produce a parse error rather than a silent
/// pass — operational tools must fail loud.</para>
/// </remarks>
public static class ArgParser
{
    public static ParseResult Parse(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return ParseResult.Help();
        }

        var head = args[0];
        if (head is "-h" or "--help" or "help")
        {
            return ParseResult.Help();
        }

        if (
            head
            is not (
                ParsedArgs.SubcommandProvision
                or ParsedArgs.SubcommandApply
                or ParsedArgs.SubcommandArchive
                or ParsedArgs.SubcommandRestore
                or ParsedArgs.SubcommandStatus
                or ParsedArgs.SubcommandSeedOwner
            )
        )
        {
            return ParseResult.Error(
                $"unknown subcommand '{head}'. Try 'shopflow-migrate --help'."
            );
        }

        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                return ParseResult.Error($"expected flag, got positional argument '{token}'.");
            }

            var name = token[2..];
            string? value = null;

            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                value = name[(eq + 1)..];
                name = name[..eq];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (string.IsNullOrEmpty(name))
            {
                return ParseResult.Error($"empty flag name at position {i}.");
            }

            if (flags.ContainsKey(name))
            {
                return ParseResult.Error($"duplicate flag '--{name}'.");
            }

            flags[name] = value;
        }

        return ParseResult.Ok(new ParsedArgs(head, flags));
    }
}

public sealed record ParsedArgs(string Subcommand, IReadOnlyDictionary<string, string?> Flags)
{
    public const string SubcommandProvision = "provision";
    public const string SubcommandApply = "apply";
    public const string SubcommandArchive = "archive";
    public const string SubcommandRestore = "restore";
    public const string SubcommandStatus = "status";
    public const string SubcommandSeedOwner = "seed-owner";

    public bool HasFlag(string name) => Flags.ContainsKey(name);

    public string? GetFlag(string name) => Flags.TryGetValue(name, out var v) ? v : null;

    public string RequireFlag(string name) =>
        GetFlag(name)
        ?? throw new InvalidOperationException($"required flag '--{name}' is missing.");
}

public sealed record ParseResult(ParsedArgs? Args, string? ErrorMessage, bool ShowHelp)
{
    public bool IsOk => Args is not null && !ShowHelp;

    public static ParseResult Ok(ParsedArgs args) => new(args, null, false);

    public static ParseResult Error(string message) => new(null, message, false);

    public static ParseResult Help() => new(null, null, true);
}
