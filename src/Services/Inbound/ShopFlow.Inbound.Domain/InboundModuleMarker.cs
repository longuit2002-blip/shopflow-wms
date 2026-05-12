namespace ShopFlow.Inbound.Domain;

/// <summary>
/// Placeholder so the Inbound.Domain csproj has at least one type and the
/// module shape is locked into CI (plan U9). Phase-1 Sprint-2 introduces
/// the actual aggregates (RawWebhook, ProcessedWebhook). Delete this
/// marker when the first real type lands; it has no behavior.
/// </summary>
public static class InboundModuleMarker
{
    public const string ModuleName = "Inbound";
}
