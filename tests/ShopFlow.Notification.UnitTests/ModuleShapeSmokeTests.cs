using ShopFlow.Notification.Application;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure;

namespace ShopFlow.Notification.UnitTests;

/// <summary>
/// Smoke tests guarding the Sprint-9.5 U1 Notification module shape.
/// Phase-0-redux U9 precedent — each business module carries a smoke
/// test that locks the project layout + canonical type names so a
/// project rename or accidental delete surfaces in CI before downstream
/// consumers silently break (composition root, observability labels,
/// catalog migration registration).
/// </summary>
public sealed class ModuleShapeSmokeTests
{
    [Fact]
    public void Application_HasMarker_WithCanonicalModuleName()
    {
        NotificationApplicationMarker.ModuleName.Should().Be("Notification");
    }

    [Fact]
    public void Application_MarkerType_LivesInExpectedNamespace()
    {
        typeof(NotificationApplicationMarker)
            .Namespace.Should()
            .Be("ShopFlow.Notification.Application");
    }

    [Fact]
    public void Infrastructure_ExposesNotificationDbContext()
    {
        typeof(NotificationDbContext).Should().NotBeNull();
        typeof(NotificationDbContext)
            .Namespace.Should()
            .Be("ShopFlow.Notification.Infrastructure");
    }

    [Fact]
    public void Domain_CarriesNotificationKindEnum()
    {
        typeof(NotificationKind).IsEnum.Should().BeTrue();
        Enum.GetNames<NotificationKind>()
            .Should()
            .BeEquivalentTo(
                new[] { "PasswordReset", "RefreshReuse", "AccountLocked", "MfaEnrolled" }
            );
    }

    [Fact]
    public void Domain_CarriesRecipientValueObject()
    {
        typeof(Recipient).Namespace.Should().Be("ShopFlow.Notification.Domain.ValueObjects");
    }

    [Fact]
    public void Domain_CarriesRenderedEmailValueObject()
    {
        typeof(RenderedEmail).Namespace.Should().Be("ShopFlow.Notification.Domain.ValueObjects");
    }

    [Fact]
    public void Domain_CarriesThreeRowEntities()
    {
        typeof(NotificationOutboxEntry)
            .Namespace.Should()
            .Be("ShopFlow.Notification.Domain.Entities");
        typeof(NotificationLogEntry)
            .Namespace.Should()
            .Be("ShopFlow.Notification.Domain.Entities");
        typeof(NotificationDeadLetterEntry)
            .Namespace.Should()
            .Be("ShopFlow.Notification.Domain.Entities");
    }
}
