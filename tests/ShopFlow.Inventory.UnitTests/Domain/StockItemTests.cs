using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Domain.Events;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class StockItemTests
{
    private static readonly Sku TestSku = Sku.Create("SKU-1");

    [Fact]
    public void Reserve_MovesUnitsFromAvailableToReserved()
    {
        var item = StockItem.Create(TestSku, Quantity.From(100));

        var result = item.Reserve(Quantity.From(10));

        result.IsSuccess.Should().BeTrue();
        item.Available.Value.Should().Be(90);
        item.Reserved.Value.Should().Be(10);
    }

    [Fact]
    public void Reserve_BeyondAvailable_FailsWithInsufficientCode()
    {
        var item = StockItem.Create(TestSku, Quantity.From(5));

        var result = item.Reserve(Quantity.From(6));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("stock.insufficient");
        item.Available.Value.Should().Be(5);
        item.Reserved.Value.Should().Be(0);
    }

    [Fact]
    public void Confirm_DecrementsReservedAndRaisesStockChangedEvent()
    {
        var item = StockItem.Create(TestSku, Quantity.From(100));
        item.Reserve(Quantity.From(10));

        var result = item.Confirm(Quantity.From(10));

        result.IsSuccess.Should().BeTrue();
        item.Reserved.Value.Should().Be(0);
        item.Available.Value.Should().Be(90);
        item.DomainEvents.Should().ContainSingle(e => e is StockChangedEvent);
    }

    [Fact]
    public void Release_ReturnsReservedToAvailable()
    {
        var item = StockItem.Create(TestSku, Quantity.From(100));
        item.Reserve(Quantity.From(10));

        var result = item.Release(Quantity.From(10));

        result.IsSuccess.Should().BeTrue();
        item.Available.Value.Should().Be(100);
        item.Reserved.Value.Should().Be(0);
    }

    [Theory]
    [InlineData(5, 105)]
    [InlineData(-5, 95)]
    public void Adjust_AppliesPositiveAndNegativeDeltas(int delta, int expectedAvailable)
    {
        var item = StockItem.Create(TestSku, Quantity.From(100));

        var result = item.Adjust(delta, StockAdjustmentReason.Receipt);

        result.IsSuccess.Should().BeTrue();
        item.Available.Value.Should().Be(expectedAvailable);
    }

    [Fact]
    public void Adjust_NegativeBeyondAvailable_FailsWithUnderflowCode()
    {
        var item = StockItem.Create(TestSku, Quantity.From(3));

        var result = item.Adjust(-10, StockAdjustmentReason.CycleCount);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("stock.adjustment_underflow");
    }
}
