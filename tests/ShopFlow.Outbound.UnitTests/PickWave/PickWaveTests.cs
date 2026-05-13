using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.UnitTests.PickWaveTests;

/// <summary>
/// Sprint-3-redux U5 — <see cref="PickWave"/> aggregate state machine.
/// Mirrors the <c>OrderTests</c> shape: every domain transition
/// exercised in isolation against the public <c>Result</c> contract.
/// </summary>
public sealed class PickWaveTests
{
    private static readonly DateTime Now = new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    // ── Open factory ------------------------------------------------------

    [Fact]
    public void Open_HappyPath_ProducesWaveWithProfileAndPicker()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);

        wave.ShippingProfile.Should().Be("standard");
        wave.PickerId.Should().Be("picker-1");
        wave.CreatedAt.Should().Be(Now);
        wave.ClosedAt.Should().BeNull();
        wave.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_BlankShippingProfile_Throws(string shippingProfile)
    {
        var act = () => PickWave.Open(shippingProfile, "picker-1", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("shippingProfile");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_BlankPickerId_Throws(string pickerId)
    {
        var act = () => PickWave.Open("standard", pickerId, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("pickerId");
    }

    [Fact]
    public void Open_TrimsProfileAndPicker()
    {
        var wave = PickWave.Open("  express  ", "  picker-2  ", Now);

        wave.ShippingProfile.Should().Be("express");
        wave.PickerId.Should().Be("picker-2");
    }

    // ── AssignOrder ------------------------------------------------------

    [Fact]
    public void AssignOrder_HappyPath_AppendsAssignment()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);
        var orderId = Guid.NewGuid();

        var result = wave.AssignOrder(orderId, Now);

        result.IsSuccess.Should().BeTrue();
        wave.Assignments.Should().HaveCount(1);
        wave.Assignments.Single().OrderId.Should().Be(orderId);
        wave.Assignments.Single().PickWaveId.Should().Be(wave.Id);
        wave.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void AssignOrder_MultipleOrders_AppendsAll()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);
        var orderIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        foreach (var oid in orderIds)
        {
            wave.AssignOrder(oid, Now).IsSuccess.Should().BeTrue();
        }

        wave.Assignments.Should().HaveCount(10);
        wave.Assignments.Select(a => a.OrderId).Should().BeEquivalentTo(orderIds);
    }

    [Fact]
    public void AssignOrder_AfterClose_Fails()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);
        wave.Close(Now);

        var result = wave.AssignOrder(Guid.NewGuid(), Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("pick_wave.closed");
        wave.Assignments.Should().BeEmpty();
    }

    [Fact]
    public void AssignOrder_EmptyOrderId_Fails()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);

        var result = wave.AssignOrder(Guid.Empty, Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("pick_wave.order_id_required");
    }

    // ── Close ------------------------------------------------------------

    [Fact]
    public void Close_HappyPath_SetsClosedAt()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);
        var closeAt = Now.AddSeconds(30);

        var result = wave.Close(closeAt);

        result.IsSuccess.Should().BeTrue();
        wave.ClosedAt.Should().Be(closeAt);
        wave.UpdatedAt.Should().Be(closeAt);
    }

    [Fact]
    public void Close_Twice_Fails()
    {
        var wave = PickWave.Open("standard", "picker-1", Now);
        wave.Close(Now);

        var result = wave.Close(Now.AddSeconds(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("pick_wave.already_closed");
    }
}
