namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Marker so the csproj has a discoverable type and CI's xunit runner
/// doesn't error on an empty assembly. Real fixtures + tests land in
/// U5 (push log persistence) and U9 (happy path + scale gate).
/// </summary>
internal static class IntegrationTestsMarker
{
    public const string ModuleName = "StockSync.Integration";
}
