# Central Package Management + Directory.Build.props — single source of truth

**Date**: 2026-04-28
**Affects**: every `.csproj`, `Directory.Build.props`, `Directory.Packages.props`

## Problem

Three separate symptoms over W0–W1, all rooted in the same cause (no centralised version management):

1. **OpenTelemetry security advisory `GHSA-4625-4j76-fww9` on 1.10.0**. Five separate `PackageReference` lines across one csproj had to be bumped to 1.15.x. Multiplied across 10 csprojs of a real codebase, this becomes a multi-day chore that is easy to do incompletely.

2. **Microsoft.CodeAnalysis 1.0.x vs 4.11.0 conflict** (CS1705). The analyzer-testing transitive deps locked at 1.0.x; the analyzer DLL itself was built on 4.11.0; NuGet resolved to the lowest-common (1.0.x) and the build broke. Required explicit higher-version pins to override transitives.

3. **Six near-identical PropertyGroup blocks duplicated across 10 csprojs** (`<TargetFramework>net8.0</TargetFramework>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<LangVersion>latest</LangVersion>`, `<IsPackable>false</IsPackable>`). Drift waiting to happen — and U6 already started showing differences (test csprojs had `<IsTestProject>true</IsTestProject>` in inconsistent positions).

## Root cause

Per-csproj package versions and per-csproj PropertyGroup settings are the .NET ecosystem's default but not the only option. They scale poorly past ~5 csprojs and lead to predictable drift.

The .NET 6+ tooling provides two solutions that were not adopted at the start:

1. **Central Package Management (CPM)** — `Directory.Packages.props` at the repo root with `<PackageVersion>` items; `<PackageReference>` in csprojs declares only `Include="..."` (no `Version`).
2. **Directory.Build.props** — repo-root or directory-scoped `.props` file whose properties auto-import into every csproj at MSBuild project-load time.

Both are MSBuild conventions discovered up the directory tree automatically. No `<Import>` needed in individual csprojs.

## Solution

Adopted as the consistency-hardening pass after U6:

- **[`Directory.Build.props`](../../Directory.Build.props)** at repo root — `TargetFramework`, `ImplicitUsings`, `Nullable`, `LangVersion`, `TreatWarningsAsErrors`, `IsPackable`, `ManagePackageVersionsCentrally`, `CentralPackageTransitivePinningEnabled`. Per-project csproj only carries divergence (the analyzer csproj overrides `TargetFramework` to `netstandard2.0`).

- **[`Directory.Packages.props`](../../Directory.Packages.props)** at repo root — every package version pinned in one file, organized by category (Domain/App, Infrastructure, Observability, API, Roslyn, Test, Integration test). Csprojs declare `<PackageReference Include="..." />` with no Version.

- **[`tests/Directory.Build.props`](../../tests/Directory.Build.props)** — test-specific overlay: imports the parent root, sets `IsTestProject`, suppresses `NU1701`/`NU1902` (third-party transitive complaints we can't control), declares the standard test packages + `<Using Include="Xunit" />` + `<Using Include="FluentAssertions" />` so test csprojs become tiny.

- **`CentralPackageTransitivePinningEnabled=true`** is the killer feature. With it, transitive dependencies are *also* pinned to the central versions, which is exactly what would have prevented the MS.CodeAnalysis 1.0.x vs 4.11.0 conflict. NuGet stops being clever and uses our explicit pins.

After consolidation, csproj sizes:

| Csproj | Before | After |
|---|---|---|
| `ShopFlow.Inventory.Domain` | 25 lines | 11 lines |
| `ShopFlow.Inventory.Application` | 28 lines | 14 lines |
| `ShopFlow.SharedKernel.UnitTests` | 75 lines | 47 lines |

## Prevention

- **Bump-package workflow**: edit one line in `Directory.Packages.props`, re-run `dotnet build`/`task ci`. Done. No grep across 10 csprojs.
- **Add-new-package workflow**: add `<PackageVersion>` to `Directory.Packages.props`, then `<PackageReference Include="..." />` (no Version) in the consuming csproj. CI fails clearly if the version is missing.
- **Add-new-module workflow** (relevant to U10 replicate × 5): copy an existing module's csproj. The module-shape canon in [`AGENTS.md`](../../AGENTS.md) §11 documents this verbatim. After CPM, the copy needs zero version edits — it inherits everything.
- **Future drift detection**: a CI step that fails if `<PackageReference Include="..." Version="..." />` (with a Version attribute) appears anywhere — that signals someone bypassed CPM. Out of scope for Phase 0; logged for U9 CI work.

## References

- `Directory.Build.props`, `Directory.Packages.props`, `tests/Directory.Build.props`
- All 10 `.csproj` files updated
- [Microsoft Learn — Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [Microsoft Learn — Directory.Build.props](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory)
