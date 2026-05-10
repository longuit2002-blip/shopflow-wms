using FluentAssertions;
using NSubstitute;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Handlers;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Domain.Events;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.UnitTests.Application;

public sealed class AdjustStockHandlerTests
{
    [Fact]
    public async Task Handle_LoadsAggregateAndCallsAdjustStockAndSaves()
    {
        var stockItems = Substitute.For<IStockItemRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var ctx = Substitute.For<IRequestContext>();
        ctx.TenantId.Returns(Guid.NewGuid());

        var aggregate = new StockItem(
            tenantId: ctx.TenantId,
            sku: new Sku("SKU-001"),
            name: "Test",
            category: null,
            totalQuantity: 100,
            safetyThreshold: 5
        );

        stockItems
            .LoadBySkuAsync(ctx.TenantId, Arg.Any<Sku>(), Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var handler = new AdjustStockHandler(stockItems, unitOfWork, ctx);
        var userId = Guid.NewGuid();

        var result = await handler.Handle(
            new AdjustStockCommand("SKU-001", +5, StockAdjustmentReason.Receiving, userId),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        aggregate.TotalQuantity.Should().Be(105);
        aggregate.DomainEvents.OfType<StockAdjustedEvent>().Should().ContainSingle();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStockItemNotFound_ReturnsNotFoundFailure()
    {
        var stockItems = Substitute.For<IStockItemRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var ctx = Substitute.For<IRequestContext>();
        ctx.TenantId.Returns(Guid.NewGuid());

        stockItems
            .LoadBySkuAsync(ctx.TenantId, Arg.Any<Sku>(), Arg.Any<CancellationToken>())
            .Returns((StockItem?)null);

        var handler = new AdjustStockHandler(stockItems, unitOfWork, ctx);

        var result = await handler.Handle(
            new AdjustStockCommand("SKU-001", +5, StockAdjustmentReason.Receiving, Guid.NewGuid()),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
