using ShopFlow.Analytics.Application;

namespace ShopFlow.Analytics.UnitTests;

/// <summary>
/// Smoke test guarding the U9 Analytics shape lock. Analytics is the
/// documented quartet exception per root AGENTS.md §11.76 — no Domain
/// project, just Application + Infrastructure + Api.
/// </summary>
public sealed class ModuleShapeSmokeTests
{
    [Fact]
    public void Application_HasPlaceholder_WithMatchingModuleName()
    {
        AnalyticsApplicationMarker.ModuleName.Should().Be("Analytics");
    }
}
