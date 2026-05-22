using System.Text.RegularExpressions;
using ShopFlow.SharedKernel.Authorization;

namespace ShopFlow.SharedKernel.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U2 — pins the frontend permission catalog
/// (<c>web/src/api/admin.ts</c>) against the backend source of truth
/// (<see cref="PermissionKeys"/>). Sprint-9.5 U7 shipped a frontend
/// catalog that had drifted ~9 entries by the time Sprint-10 attached
/// `[Authorize(Policy=...)]` to controller actions — drift on either
/// side becomes a privilege-escalation vector the moment Sprint-11's
/// non-Owner role lands.
/// </summary>
/// <remarks>
/// <para>This test is filesystem-only — it reads <c>admin.ts</c> as
/// text and parses the three known array literals
/// (<c>PERMISSION_KEYS</c>, <c>OWNER_CRITICAL_KEYS</c>,
/// <c>MODULES</c>) with regex. No Docker, no Postgres, no
/// Testcontainers; tagged <c>Category=Integration</c> only because it
/// crosses the language boundary and the per-PR CI lane is the
/// right place to fail on drift.</para>
///
/// <para><b>Regex anchoring (adv-2 / F6):</b> the catalog declaration
/// is <c>readonly { key: string; module: string }[] = [...]</c> — the
/// TypeScript type annotation contains a bare <c>[]</c> token that
/// would mismatch a naive <c>\[</c> anchor. We anchor on the
/// post-<c>= [</c> array-literal start instead. The count-assertion
/// guard (24 / 9) before the set-equality assert catches partial-set
/// parser degradation that the set-equality alone might miss.</para>
///
/// <para><b>Fact 3 (adv-7):</b> validates that every
/// <c>module:</c> value referenced inside <c>PERMISSION_KEYS</c> is
/// present in the <c>MODULES</c> array (and vice versa). A
/// misspelled module name on a single permission would silently drop
/// that key from the editor's grouped rendering — a UX bug that
/// regex set-equality alone wouldn't catch.</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class AdminTsCatalogContractTests
{
    // Count guards (adv-2 / F6) — these track the U1 commit `0de0d1c`
    // shape and the backend `PermissionKeys.cs`. Update both sides if
    // the catalog grows; the equality assert below catches the rest.
    private const int ExpectedPermissionKeyCount = 24;
    private const int ExpectedOwnerCriticalCount = 9;

    // Capture group 1 = the string literal value between matching quotes.
    // Handles both single and double quotes (TypeScript style choice).
    private static readonly Regex PermissionKeyEntryRegex = new(
        pattern: @"key:\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled);

    private static readonly Regex ModuleFieldEntryRegex = new(
        pattern: @"module:\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled);

    private static readonly Regex BareStringLiteralRegex = new(
        pattern: @"['""]([^'""]+)['""]",
        RegexOptions.Compiled);

    [Fact]
    public void PermissionKeys_All_SetEquals_AdminTsCatalog()
    {
        var adminTs = ReadAdminTs();
        var permissionKeysBlock = ExtractArrayLiteralBlock(
            adminTs,
            declarationAnchor: "export const PERMISSION_KEYS:");

        var frontendKeys = PermissionKeyEntryRegex
            .Matches(permissionKeysBlock)
            .Select(m => m.Groups[1].Value)
            .ToList();

        // Count guard (adv-2 / F6) — catches the case where the regex
        // partially degrades and returns 3-of-24 instead of failing.
        frontendKeys.Should().HaveCount(
            ExpectedPermissionKeyCount,
            because: "PERMISSION_KEYS in admin.ts must declare exactly "
                + $"{ExpectedPermissionKeyCount} entries (Sprint-10.5 U1 shape); "
                + "if this count changed intentionally, update "
                + "ExpectedPermissionKeyCount AND PermissionKeys.cs together");

        var frontendSet = frontendKeys.ToHashSet(StringComparer.Ordinal);
        var backendSet = PermissionKeys.All.ToHashSet(StringComparer.Ordinal);

        var missingFromFrontend = backendSet.Except(frontendSet).OrderBy(x => x).ToList();
        var extraInFrontend = frontendSet.Except(backendSet).OrderBy(x => x).ToList();

        (missingFromFrontend.Count + extraInFrontend.Count).Should().Be(
            0,
            because: BuildDriftMessage(
                surface: "PERMISSION_KEYS",
                missingFromFrontend: missingFromFrontend,
                extraInFrontend: extraInFrontend));
    }

    [Fact]
    public void OwnerCritical_SetEquals_AdminTsCatalog()
    {
        var adminTs = ReadAdminTs();
        var ownerCriticalBlock = ExtractArrayLiteralBlock(
            adminTs,
            declarationAnchor: "export const OWNER_CRITICAL_KEYS:");

        var frontendKeys = BareStringLiteralRegex
            .Matches(ownerCriticalBlock)
            .Select(m => m.Groups[1].Value)
            .ToList();

        // Count guard (adv-2 / F6).
        frontendKeys.Should().HaveCount(
            ExpectedOwnerCriticalCount,
            because: "OWNER_CRITICAL_KEYS in admin.ts must declare exactly "
                + $"{ExpectedOwnerCriticalCount} entries (Sprint-9 KTD13 shape); "
                + "if this count changed intentionally, update "
                + "ExpectedOwnerCriticalCount AND PermissionKeys.OwnerCritical together");

        var frontendSet = frontendKeys.ToHashSet(StringComparer.Ordinal);
        var backendSet = PermissionKeys.OwnerCritical.ToHashSet(StringComparer.Ordinal);

        var missingFromFrontend = backendSet.Except(frontendSet).OrderBy(x => x).ToList();
        var extraInFrontend = frontendSet.Except(backendSet).OrderBy(x => x).ToList();

        (missingFromFrontend.Count + extraInFrontend.Count).Should().Be(
            0,
            because: BuildDriftMessage(
                surface: "OWNER_CRITICAL_KEYS",
                missingFromFrontend: missingFromFrontend,
                extraInFrontend: extraInFrontend));
    }

    [Fact]
    public void AdminTsModules_AreReferentiallyConsistent()
    {
        var adminTs = ReadAdminTs();

        var permissionKeysBlock = ExtractArrayLiteralBlock(
            adminTs,
            declarationAnchor: "export const PERMISSION_KEYS:");
        var modulesBlock = ExtractArrayLiteralBlock(
            adminTs,
            declarationAnchor: "export const MODULES:");

        var modulesReferencedByKeys = ModuleFieldEntryRegex
            .Matches(permissionKeysBlock)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var modulesDeclared = BareStringLiteralRegex
            .Matches(modulesBlock)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var unknownModuleReferences = modulesReferencedByKeys
            .Except(modulesDeclared)
            .OrderBy(x => x)
            .ToList();
        var unreferencedDeclaredModules = modulesDeclared
            .Except(modulesReferencedByKeys)
            .OrderBy(x => x)
            .ToList();

        unknownModuleReferences.Should().BeEmpty(
            because: "every `module:` value used in PERMISSION_KEYS must also "
                + "appear in the MODULES array, otherwise that key will silently "
                + "disappear from the RolePermissionsEditor's grouped rendering. "
                + $"Unknown module references: [{string.Join(", ", unknownModuleReferences)}]");

        unreferencedDeclaredModules.Should().BeEmpty(
            because: "every entry in MODULES must be referenced by at least one "
                + "PERMISSION_KEYS row, otherwise the editor renders an empty "
                + "section header. Orphan modules in MODULES: "
                + $"[{string.Join(", ", unreferencedDeclaredModules)}]");
    }

    private static string BuildDriftMessage(
        string surface,
        IReadOnlyList<string> missingFromFrontend,
        IReadOnlyList<string> extraInFrontend)
    {
        var missingPart = missingFromFrontend.Count == 0
            ? "<none>"
            : $"[{string.Join(", ", missingFromFrontend)}]";
        var extraPart = extraInFrontend.Count == 0
            ? "<none>"
            : $"[{string.Join(", ", extraInFrontend)}]";
        return $"{surface} drift between backend PermissionKeys and admin.ts. "
            + $"Missing from admin.ts (declared backend-side, absent frontend-side): {missingPart}. "
            + $"Extra in admin.ts (declared frontend-side, absent backend-side): {extraPart}. "
            + "Update both sides together — a divergence here becomes a "
            + "privilege-escalation vector when non-Owner roles ship.";
    }

    /// <summary>
    /// Extracts the text of an array literal that follows the given
    /// declaration anchor, bounded by the matching <c>]</c>.
    /// </summary>
    /// <remarks>
    /// <para>Anchors on <c>= [</c> (the post-equals array-literal
    /// start), NOT on the first <c>[</c> after the declaration name —
    /// the TypeScript type annotation
    /// <c>readonly { key: string; module: string }[]</c> contains a
    /// bare <c>[]</c> that would mismatch the naive anchor (adv-2 /
    /// F6).</para>
    /// </remarks>
    private static string ExtractArrayLiteralBlock(string source, string declarationAnchor)
    {
        var declIdx = source.IndexOf(declarationAnchor, StringComparison.Ordinal);
        if (declIdx < 0)
        {
            throw new InvalidOperationException(
                $"Anchor not found in admin.ts: '{declarationAnchor}'. "
                + "Did the U1 declaration shape change?");
        }

        // Find the post-equals array literal start.
        var equalsBracketIdx = source.IndexOf("= [", declIdx, StringComparison.Ordinal);
        if (equalsBracketIdx < 0)
        {
            throw new InvalidOperationException(
                $"'= [' not found after anchor '{declarationAnchor}'. "
                + "The declaration is expected to be `export const NAME: <type> = [...]`. "
                + "If a formatter split the `=` and `[` onto different lines, this "
                + "regex needs updating.");
        }

        var arrayStartIdx = equalsBracketIdx + 2; // index of '['

        // Bracket-balance scan to find matching ']'. The catalog rows
        // are simple object literals — no nested arrays — so this is
        // a straight depth counter, not a full JS parser.
        var depth = 0;
        for (var i = arrayStartIdx; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(arrayStartIdx, i - arrayStartIdx + 1);
                }
            }
        }

        throw new InvalidOperationException(
            $"Unbalanced brackets after anchor '{declarationAnchor}'. "
            + "Could not find the matching ']' for the array literal start.");
    }

    private static string ReadAdminTs()
    {
        var repoRoot = FindRepoRoot();
        var adminTsPath = Path.Combine(repoRoot, "web", "src", "api", "admin.ts");
        if (!File.Exists(adminTsPath))
        {
            throw new FileNotFoundException(
                $"web/src/api/admin.ts not found at expected path '{adminTsPath}'. "
                + "Was the file moved? Did Sprint-10.5 U1 land at the wrong path?",
                adminTsPath);
        }
        return File.ReadAllText(adminTsPath);
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking
    /// for <c>ShopFlow.sln</c> (the repo-root sentinel). Test runners
    /// land the working directory under
    /// <c>bin/Debug/net9.0/</c> so the walk-up is mandatory.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ShopFlow.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new InvalidOperationException(
                "Repo root not found (looked for ShopFlow.sln walking up from "
                + AppContext.BaseDirectory + "). This test must run from inside the "
                + "ShopFlow.sln tree.");
        }
        return dir.FullName;
    }
}
