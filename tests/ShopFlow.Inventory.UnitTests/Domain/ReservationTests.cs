using FluentAssertions;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class ReservationTests
{
    private static Reservation MakeReservation(
        ReservationStatus status = ReservationStatus.Active,
        DateTime? expiresAt = null
    )
    {
        var reservedAt = new DateTime(2026, 4, 27, 12, 0, 0, DateTimeKind.Utc);
        return new Reservation(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Sku: "SKU-001",
            Qty: 1,
            OrderId: Guid.NewGuid(),
            Status: status,
            ReservedAt: reservedAt,
            ExpiresAt: expiresAt ?? reservedAt.AddMinutes(15),
            FinalizedAt: null
        );
    }

    [Fact]
    public void IsActive_ActiveStatusBeforeExpiry_ReturnsTrue()
    {
        var expiry = new DateTime(2026, 4, 27, 12, 15, 0, DateTimeKind.Utc);
        var reservation = MakeReservation(ReservationStatus.Active, expiry);
        var now = expiry.AddMinutes(-1);

        reservation.IsActive(now).Should().BeTrue();
    }

    [Fact]
    public void IsActive_ActiveStatusAtOrAfterExpiry_ReturnsFalse()
    {
        var expiry = new DateTime(2026, 4, 27, 12, 15, 0, DateTimeKind.Utc);
        var reservation = MakeReservation(ReservationStatus.Active, expiry);

        reservation.IsActive(expiry).Should().BeFalse();
        reservation.IsActive(expiry.AddSeconds(1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void IsActive_NonActiveStatus_AlwaysFalse(ReservationStatus status)
    {
        var reservation = MakeReservation(status);
        var now = reservation.ReservedAt;

        reservation.IsActive(now).Should().BeFalse();
    }
}
