namespace ShopFlow.Notification.Application;

/// <summary>
/// Module marker. Carries the canonical module name string consumed by
/// <c>ModuleShapeSmokeTests</c> (Phase-0-redux U9 pattern). The smoke
/// test guards against accidental project-rename drift — anything that
/// would shift <see cref="ModuleName"/> off "Notification" surfaces
/// before downstream consumers (catalog registration, observability
/// labels, log scoping) silently break.
/// </summary>
public static class NotificationApplicationMarker
{
    public const string ModuleName = "Notification";
}
