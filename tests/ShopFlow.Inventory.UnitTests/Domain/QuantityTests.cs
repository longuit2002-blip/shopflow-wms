using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class QuantityTests
{
    [Fact]
    public void Zero_IsSharedAndEqualsFromZero()
    {
        Quantity.Zero.Value.Should().Be(0);
        Quantity.Zero.Should().Be(Quantity.From(0));
    }

    [Fact]
    public void From_NegativeValue_Throws()
    {
        var act = () => Quantity.From(-1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Fact]
    public void Add_ProducesSum()
    {
        var sum = Quantity.From(3).Add(Quantity.From(4));
        sum.Value.Should().Be(7);
    }

    [Fact]
    public void Subtract_WithSufficientQuantity_ProducesDifference()
    {
        var result = Quantity.From(10).Subtract(Quantity.From(3));
        result.Value.Should().Be(7);
    }

    [Fact]
    public void Subtract_WhenUnderflows_Throws()
    {
        var act = () => Quantity.From(3).Subtract(Quantity.From(4));
        act.Should().Throw<InvalidOperationException>().WithMessage("*underflow*");
    }
}
