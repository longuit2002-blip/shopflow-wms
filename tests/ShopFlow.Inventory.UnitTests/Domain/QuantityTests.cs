using FluentAssertions;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class QuantityTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsNegative(int value)
    {
        var act = () => new Quantity(value);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Constructor_AcceptsNonNegative(int value)
    {
        var qty = new Quantity(value);
        qty.Value.Should().Be(value);
    }

    [Fact]
    public void Zero_IsSingletonValueObject()
    {
        Quantity.Zero.Value.Should().Be(0);
        Quantity.Zero.Should().Be(new Quantity(0));
    }
}
