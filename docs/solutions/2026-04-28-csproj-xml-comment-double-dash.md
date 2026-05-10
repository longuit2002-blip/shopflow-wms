# XML comments forbid `--` — escapes shell-flag examples in csproj/props

**Date**: 2026-04-28
**Affects**: every `.csproj`, every `Directory.Build.props`, every `Directory.Packages.props`, every other XML file

## Problem

Hit twice in two days during the Phase-0 bootstrap:

1. **U6** — `tests/ShopFlow.Inventory.IntegrationTests/ShopFlow.Inventory.IntegrationTests.csproj` had a comment line `(\`dotnet test --filter "Category!=Integration"\`)`. Build failed with:

   ```
   error MSB4025: The project file could not be loaded. An XML comment cannot contain '--', and '-' cannot be the last character.
   ```

2. **Consistency hardening pass** — wrote a brand-new `Directory.Packages.props` with a comment line `re-running \`dotnet csharpier --check .\``. Build failed with the *exact same error*. Identical lesson, second occurrence in 24 hours.

## Root cause

XML 1.0 specification forbids `--` inside `<!-- ... -->` comments. Reason: the spec defines `-->` as the comment terminator, and disallowing `--` inside the body removes ambiguity. MSBuild's XML parser enforces this strictly.

Common triggers in csproj/props comments:

- `--filter`, `--check`, `--no-restore`, `--verbose` — any CLI flag with a long option
- `dotnet test --foo`, `csharpier --bar`
- ASCII separators like `===========` (safe — that's `=` not `-`) but `-----------` (NOT safe; that's literal `--` repeated)
- `--` as a markdown em-dash convention (use Unicode `—` U+2014 instead)

## Solution

Inside any XML comment, choose ONE of:

1. **Rephrase** to avoid the literal `--`. Most commonly: drop the flag example or describe the behaviour in prose.

   ```xml
   <!-- BAD:  Run dotnet csharpier --check . to verify formatting. -->
   <!-- GOOD: Run CSharpier in check mode to verify formatting (see Taskfile). -->
   ```

2. **Use a Unicode em-dash** (U+2014, `—`) where the intent was a typographic dash. Already the convention throughout [`AGENTS.md`](../../AGENTS.md).

3. **Reference an external doc** instead of inlining the command:

   ```xml
   <!-- See Taskfile.yml `pre-commit` task for the exact invocation. -->
   ```

## Prevention

- **CSharpier doesn't lint XML files**, so the only enforcement is the build itself. The pre-commit hook runs `dotnet csharpier --check .` which catches `.cs` formatting but not csproj XML errors.
- A `dotnet build` against a freshly cloned repo will surface this at the project-load stage. CI's restore + build step is the safety net.
- Mental model: any time you write a CLI flag inside an XML comment, rephrase or escape. The pattern occurs naturally because we describe shell commands in csproj comments — common impulse, common tripwire.
- Consider a future addition: a Roslyn analyzer / MSBuild target that scans `.csproj` and `.props` files for the pattern. Out of scope for Phase 0.

## References

- XML 1.0 spec, §2.5 Comments — defines the rule
- `tests/ShopFlow.Inventory.IntegrationTests/ShopFlow.Inventory.IntegrationTests.csproj` — first hit (U6 commit `6599507`)
- `Directory.Packages.props` — second hit (consistency hardening pass)
