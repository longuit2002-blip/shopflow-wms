using FluentAssertions;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Domain.Events;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class StockItemTests
{
    private static StockItem MakeItem(int total = 50, int safety = 5) =>
        new(
            tenantId: Guid.NewGuid(),
            sku: new Sku("SKU-001"),
            name: "Test SKU",
            category: "test",
            totalQuantity: total,
            safetyThreshold: safety
        );

    [Fact]
    public void AdjustStock_PositiveDelta_RaisesStockAdjustedEventWithCorrectDelta()
    {
        var item = MakeItem(total: 100);
        var userId = Guid.NewGuid();

        item.AdjustStock(+10, StockAdjustmentReason.Receiving, userId);

        item.TotalQuantity.Should().Be(110);
        item.DomainEvents.Should().ContainSingle();
        var raised = item.DomainEvents.OfType<StockAdjustedEvent>().Single();
        raised.Delta.Should().Be(10);
        raised.NewTotalQuantity.Should().Be(110);
        raised.Reason.Should().Be(StockAdjustmentReason.Receiving);
        raised.UserId.Should().Be(userId);
    }

    [Fact]
    public void AdjustStock_LargeNegativeDelta_ClampsTotalQuantityAtZero()
    {
        var item = MakeItem(total: 50);

        item.AdjustStock(-1000, StockAdjustmentReason.StockTake, Guid.NewGuid());

        item.TotalQuantity.Should().Be(0);
        var raised = item.DomainEvents.OfType<StockAdjustedEvent>().Single();
        raised.Delta.Should().Be(-1000); // event preserves the requested delta
        raised.NewTotalQuantity.Should().Be(0);
    }

    [Fact]
    public void ConfirmDeduction_RaisesStockChangedEventWithDeductedTotal()
    {
        var item = MakeItem(total: 30);

        item.ConfirmDeduction(5);

        item.TotalQuantity.Should().Be(25);
        var raised = item.DomainEvents.OfType<StockChangedEvent>().Single();
        raised.NewTotalQuantity.Should().Be(25);
    }

    [Fact]
    public void ConfirmDeduction_NegativeQty_Throws()
    {
        var item = MakeItem();
        var act = () => item.ConfirmDeduction(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
