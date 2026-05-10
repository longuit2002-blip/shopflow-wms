# Test csproj conventions — implicit usings, NU1701/NU1902 NoWarn, IActionResult assembly

**Date**: 2026-04-28
**Affects**: every test `.csproj`, [`tests/Directory.Build.props`](../../tests/Directory.Build.props)

## Problem

Three subtle issues, all surfaced during U5 + U6:

1. **xUnit attributes "not found"**. U6's test csprojs had `<ImplicitUsings>enable</ImplicitUsings>` set, but when test files used `[Fact]`, `[Theory]`, `[InlineData]` without `using Xunit;`, the build emitted 62 errors of the form:

   ```
   error CS0246: The type or namespace name 'Fact' could not be found
   ```

2. **`NU1701` warning-as-error from analyzer-testing transitive deps**. The `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` 1.1.2 package transitively pulls in `Microsoft.CodeAnalysis.Common 1.0.1` (a 2015 release) via netframework targets. Under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, NU1701 ("package was restored using older target framework") becomes 6 build errors.

3. **`IActionResult` "not found" in analyzer test snippets**. Test sources for `MissingIdempotentAnalyzer` referenced `IActionResult` via `[HttpPost]` controller patterns. The analyzer test harness did add `Microsoft.AspNetCore.Mvc.HttpPostAttribute` and `Microsoft.AspNetCore.Mvc.ControllerBase` references — but `IActionResult` lives in **`Microsoft.AspNetCore.Mvc.Abstractions.dll`**, a separate assembly. 4 tests failed.

## Root cause

Each problem reflects an MSBuild/.NET-stack default that doesn't quite fit a multi-test-project repo:

1. `<ImplicitUsings>enable</ImplicitUsings>` adds the .NET BCL set (System, System.Linq, etc.) but NOT `Xunit` or `FluentAssertions`. Two valid fixes: `using Xunit;` per file, or `<Using Include="Xunit" />` at csproj level. The first is per-file noise; the second scales.

2. `TreatWarningsAsErrors=true` is the right default for production code (forces our own code to be clean) but penalizes test projects for choices made by their third-party deps. Suppressing NU1701 (and now NU1902 — the OpenTelemetry advisory) at test scope is the right scope.

3. ASP.NET Core's MVC stack is split across multiple assemblies. `Microsoft.AspNetCore.Mvc.Core` has the attributes and base classes; `Microsoft.AspNetCore.Mvc.Abstractions` has the abstractions interfaces (`IActionResult`, `IFilterMetadata`, etc.). Roslyn analyzer testing requires explicit `MetadataReference` for every assembly the test snippet uses.

## Solution

[`tests/Directory.Build.props`](../../tests/Directory.Build.props) consolidates all three fixes for every test csproj in the repo:

```xml
<PropertyGroup>
  <IsTestProject>true</IsTestProject>
  <NoWarn>$(NoWarn);NU1701;NU1902</NoWarn>
</PropertyGroup>

<ItemGroup>
  <Using Include="Xunit" />
  <Using Include="FluentAssertions" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="FluentAssertions" />
</ItemGroup>
```

For the IActionResult-specific issue in analyzer tests, [`AnalyzerTestHelpers.cs`](../../tests/ShopFlow.SharedKernel.UnitTests/Analyzers/AnalyzerTestHelpers.cs) explicitly adds the Mvc.Abstractions reference:

```csharp
AddReference(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute));    // Mvc.Core
AddReference(typeof(Microsoft.AspNetCore.Mvc.ControllerBase));        // Mvc.Core
AddReference(typeof(Microsoft.AspNetCore.Mvc.IActionResult));         // Mvc.Abstractions  ← key fix
```

When adding analyzer tests that reference new ASP.NET types, check which assembly they live in (`typeof(X).Assembly.GetName().Name` shows it) and add a corresponding `AddReference(typeof(X))` line.

## Prevention

- **Test csprojs are now tiny** (12–25 lines vs the previous 50+). Anyone adding a new test project just creates `<Project Sdk="Microsoft.NET.Sdk">` + the project-specific `<PackageReference>` extras + `<ProjectReference>` items. Everything else inherits.
- **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` stays on at the repo level** so production code remains strict. Test projects scope their NoWarns to known-benign third-party noise.
- **Analyzer test snippet failures**: when a snippet uses a type that "should" exist but doesn't compile, first check which assembly the type lives in via `typeof(X).Assembly`. Add the reference to the test harness, don't rewrite the snippet.

## References

- `tests/Directory.Build.props` — the consolidation
- `tests/ShopFlow.SharedKernel.UnitTests/Analyzers/AnalyzerTestHelpers.cs` — IActionResult fix
- See also: [2026-04-28-central-package-management.md](2026-04-28-central-package-management.md) for the broader CPM context
