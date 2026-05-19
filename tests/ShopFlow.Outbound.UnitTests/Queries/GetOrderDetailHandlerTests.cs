using NSubstitute;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Queries;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.UnitTests.Queries;

/// <summary>
/// Sprint-7 plan U3 — <see cref="GetOrderDetailHandler"/> coverage. Pins the
/// happy-path materialisation (Order + Lines + saga state) and the
/// <c>order.not_found</c> failure return.
/// </summary>
public sealed class GetOrderDetailHandlerTests
{
    private static Order BuildOrder(
        string externalId = "SHOPEE_001",
        params (string Sku, int Qty, int? Weight)[] lines)
    {
        var lineList = lines.Length == 0
            ? new[] { ("SKU-1", 2, (int?)null) }
            : lines.Select(l => (l.Sku, l.Qty, l.Weight)).ToArray();
        var result = Order.Create(externalId, "STANDARD", lineList);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    [Fact]
    public async Task Handle_ExistingOrder_ReturnsDetail_WithLinesAndSagaState()
    {
        var order = BuildOrder("SHOPEE_42",
            ("SKU-1", 1, 100),
            ("SKU-2", 3, 200),
            ("SKU-3", 2, 50));

        var repo = Substitute.For<IOrderRepository>();
        repo.FindByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        repo.GetCurrentSagaStateAsync(order.Id, Arg.Any<CancellationToken>()).Returns("AwaitingPick");

        var sut = new GetOrderDetailHandler(repo);

        var result = await sut.Handle(new GetOrderDetailQuery(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var detail = result.Value!;
        detail.Id.Should().Be(order.Id);
        detail.ChannelExternalOrderId.Should().Be("SHOPEE_42");
        detail.Channel.Should().Be("Shopee");
        detail.ShippingProfile.Should().Be("STANDARD");
        detail.Status.Should().Be("Created");
        detail.CurrentSagaState.Should().Be("AwaitingPick");
        detail.Lines.Should().HaveCount(3);
        detail.Lines.Select(l => l.Sku).Should().BeEquivalentTo(new[] { "SKU-1", "SKU-2", "SKU-3" });
        detail.Lines.Single(l => l.Sku == "SKU-2").Qty.Should().Be(3);
        detail.Lines.Single(l => l.Sku == "SKU-2").ExpectedWeight.Should().Be(200);
    }

    [Fact]
    public async Task Handle_MissingOrder_ReturnsFailureNotFound()
    {
        var repo = Substitute.For<IOrderRepository>();
        repo.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        var sut = new GetOrderDetailHandler(repo);

        var missingId = Guid.NewGuid();
        var result = await sut.Handle(new GetOrderDetailQuery(missingId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.not_found");
        result.Error.Should().Contain(missingId.ToString());
        await repo.DidNotReceive().GetCurrentSagaStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderWithoutSagaRow_ReturnsNullCurrentSagaState()
    {
        var order = BuildOrder("LAZADA_77");
        var repo = Substitute.For<IOrderRepository>();
        repo.FindByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        repo.GetCurrentSagaStateAsync(order.Id, Arg.Any<CancellationToken>()).Returns((string?)null);

        var sut = new GetOrderDetailHandler(repo);

        var result = await sut.Handle(new GetOrderDetailQuery(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CurrentSagaState.Should().BeNull();
        result.Value.Channel.Should().Be("Lazada");
    }

    [Fact]
    public async Task Handle_NullRequest_Throws()
    {
        var repo = Substitute.For<IOrderRepository>();
        var sut = new GetOrderDetailHandler(repo);

        Func<Task> act = () => sut.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
