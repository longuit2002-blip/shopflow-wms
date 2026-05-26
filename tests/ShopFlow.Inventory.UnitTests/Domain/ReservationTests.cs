using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class ReservationTests
{
    private static readonly Sku TestSku = Sku.Create("SKU-1");
    private static readonly DateTime Now = new(2026, 5, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidArgs_ProducesPendingReservation()
    {
        var ttl = TimeSpan.FromMinutes(15);

        var result = Reservation.Create(TestSku, "ORDER-1", Quantity.From(2), ttl, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(ReservationStatus.Pending);
        result.Value.Sku.Should().Be(TestSku);
        result.Value.OrderId.Should().Be("ORDER-1");
        result.Value.Quantity.Value.Should().Be(2);
        result.Value.ExpiresAt.Should().Be(Now + ttl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankOrderId_FailsWithCode(string orderId)
    {
        var result = Reservation.Create(
            TestSku,
            orderId,
            Quantity.From(1),
            TimeSpan.FromMinutes(15),
            Now
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.order_id_required");
    }

    [Fact]
    public void Create_ZeroQuantity_FailsWithCode()
    {
        var result = Reservation.Create(
            TestSku,
            "ORDER-1",
            Quantity.Zero,
            TimeSpan.FromMinutes(15),
            Now
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.quantity_zero");
    }

    [Fact]
    public void Create_NonPositiveTtl_FailsWithCode()
    {
        var result = Reservation.Create(TestSku, "ORDER-1", Quantity.From(1), TimeSpan.Zero, Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.ttl_non_positive");
    }

    [Fact]
    public void Confirm_OnPendingReservation_FlipsToConfirmed()
    {
        var reservation = Reservation
            .Create(TestSku, "ORDER-1", Quantity.From(1), TimeSpan.FromMinutes(15), Now)
            .Value!;

        var result = reservation.Confirm(Now);

        result.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
        reservation.ConfirmedAt.Should().Be(Now);
    }

    [Fact]
    public void Confirm_OnAlreadyConfirmed_FailsWithCode()
    {
        var reservation = Reservation
            .Create(TestSku, "ORDER-1", Quantity.From(1), TimeSpan.FromMinutes(15), Now)
            .Value!;
        reservation.Confirm(Now);

        var result = reservation.Confirm(Now.AddSeconds(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.already_confirmed");
    }

    [Fact]
    public void Release_OnPendingReservation_FlipsToReleased()
    {
        var reservation = Reservation
            .Create(TestSku, "ORDER-1", Quantity.From(1), TimeSpan.FromMinutes(15), Now)
            .Value!;

        var result = reservation.Release(Now);

        result.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.Released);
        reservation.ReleasedAt.Should().Be(Now);
    }

    [Fact]
    public void Release_OnConfirmed_FailsWithInvalidStateCode()
    {
        var reservation = Reservation
            .Create(TestSku, "ORDER-1", Quantity.From(1), TimeSpan.FromMinutes(15), Now)
            .Value!;
        reservation.Confirm(Now);

        var result = reservation.Release(Now.AddSeconds(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.invalid_state");
    }

    [Fact]
    public void Expire_OnExpiredPendingReservation_FlipsToExpired()
    {
        var ttl = TimeSpan.FromMinutes(15);
        var reservation = Reservation.Create(TestSku, "ORDER-1", Quantity.From(1), ttl, Now).Value!;
        var afterTtl = Now + ttl + TimeSpan.FromSeconds(1);

        var result = reservation.Expire(afterTtl);

        result.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.Expired);
        reservation.ExpiredAt.Should().Be(afterTtl);
    }

    [Fact]
    public void Expire_BeforeTtl_FailsWithCode()
    {
        var reservation = Reservation
            .Create(TestSku, "ORDER-1", Quantity.From(1), TimeSpan.FromMinutes(15), Now)
            .Value!;

        var result = reservation.Expire(Now.AddSeconds(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.not_yet_expired");
    }
}
