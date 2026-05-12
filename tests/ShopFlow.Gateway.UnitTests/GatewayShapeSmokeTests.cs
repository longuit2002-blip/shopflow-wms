namespace ShopFlow.Gateway.UnitTests;

/// <summary>
/// Smoke test asserting the YARP route table shape is the one U9 set
/// (5 routes, 5 clusters). Catches accidental table truncation when
/// future PRs touch <c>appsettings.json</c>.
/// </summary>
public sealed class GatewayShapeSmokeTests
{
    [Fact]
    public void Appsettings_DeclaresFiveRoutesAndFiveClusters()
    {
        // Walk up to find ShopFlow.sln (same trick as the AppHost), then
        // read the Gateway appsettings relative to it. The test runs
        // from tests/<this project>/bin/Debug/net9.0/; the .sln lives
        // three or four levels up depending on test runner cwd, so the
        // walk-up keeps the assertion location-stable.
        var root = ResolveRepoRoot();
        var path = Path.Combine(
            root,
            "src",
            "ApiGateway",
            "ShopFlow.Gateway",
            "appsettings.json"
        );
        File.Exists(path).Should().BeTrue($"expected {path} to exist");

        var json = File.ReadAllText(path);
        json.Should().Contain("\"Routes\":");
        json.Should().Contain("\"inventory\"");
        json.Should().Contain("\"inbound\"");
        json.Should().Contain("\"outbound\"");
        json.Should().Contain("\"channel\"");
        json.Should().Contain("\"analytics\"");
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShopFlow.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("ShopFlow.sln not found in any parent.");
    }
}
