using ShopFlow.Inbound.Infrastructure;

namespace ShopFlow.Inbound.UnitTests;

/// <summary>
/// Smoke test guarding the Inbound module shape. Asserts the composition
/// entry point exposes its <c>ModuleName</c> constant per AGENTS.md §11.79.
/// Real Domain + Application behavior tests live in
/// <c>tests/ShopFlow.Inbound.UnitTests/Domain/</c> +
/// <c>tests/ShopFlow.Inbound.IntegrationTests/</c> as they land per Sprint-2-redux U2-U9.
/// </summary>
public sealed class ModuleShapeSmokeTests
{
    [Fact]
    public void InboundServiceCollectionExtensions_ExposesExpectedModuleName()
    {
        InboundServiceCollectionExtensions.ModuleName.Should().Be("Inbound");
    }
}
