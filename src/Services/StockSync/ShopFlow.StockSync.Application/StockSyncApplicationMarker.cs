namespace ShopFlow.StockSync.Application;

/// <summary>
/// Marker so the StockSync.Application csproj has at least one type and the
/// module shape is locked into CI (Sprint-5 U1). Real consumer + coalescer
/// + ports land in Sprint-5 U2-U7.
/// </summary>
public static class StockSyncApplicationMarker
{
    public const string ModuleName = "StockSync";
}
