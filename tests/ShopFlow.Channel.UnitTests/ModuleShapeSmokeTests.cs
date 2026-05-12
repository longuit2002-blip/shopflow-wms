using ShopFlow.Channel.Domain;
using ShopFlow.Channel.Application;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Smoke test guarding the U9 module-shape lock. As soon as the first
/// real type lands in Phase-1+, replace these with real coverage and
/// delete the marker classes; the test compile-time-fails the moment
/// the marker is removed, which is the prompt to write real tests.
/// </summary>
public sealed class ModuleShapeSmokeTests
{
    [Fact]
    public void DomainAndApplication_HavePlaceholders_WithMatchingModuleName()
    {
        ChannelModuleMarker.ModuleName.Should().Be("Channel");
        ChannelApplicationMarker.ModuleName.Should().Be("Channel");
    }
}
