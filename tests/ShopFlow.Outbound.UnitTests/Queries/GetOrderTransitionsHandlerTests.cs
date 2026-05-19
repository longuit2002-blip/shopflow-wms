using NSubstitute;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Queries;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.UnitTests.Queries;

/// <summary>
/// Sprint-7 plan U3 — <see cref="GetOrderTransitionsHandler"/> coverage.
/// The repository already orders rows ASC by <c>occurred_at</c>, so the
/// handler is a thin projection. Tests confirm the projection preserves
/// every field + the iteration order.
/// </summary>
public sealed class GetOrderTransitionsHandlerTests
{
    private static readonly DateTime BaseTime = new(2026, 5, 19, 10, 0, 0, DateTimeKind.Utc);

    private static OrderTransition BuildTransition(
        Guid orderId,
        string fromState,
        string toState,
        int offsetSeconds,
        string eventType = "StockReservedV1",
        string? correlationId = null)
    {
        return OrderTransition.Create(
            orderId: orderId,
            fromState: fromState,
            toState: toState,
            occurredAt: BaseTime.AddSeconds(offsetSeconds),
            eventType: eventType,
            correlationId: correlationId ?? Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task Handle_SevenTransitions_ReturnsAllInRepositoryOrder()
    {
        var orderId = Guid.NewGuid();
        var rows = new List<OrderTransition>
        {
            BuildTransition(orderId, "Created", "AwaitingReservation", 0, "OrderPlacedV1"),
            BuildTransition(orderId, "AwaitingReservation", "Reserved", 1, "StockReservedV1"),
            BuildTransition(orderId, "Reserved", "AwaitingPick", 2, "ReservedToAwaitingPick"),
            BuildTransition(orderId, "AwaitingPick", "Picked", 3, "PickConfirmed"),
            BuildTransition(orderId, "Picked", "AwaitingPack", 4, "PickedToAwaitingPack"),
            BuildTransition(orderId, "AwaitingPack", "Packed", 5, "PackConfirmed"),
            BuildTransition(orderId, "Packed", "Shipped", 6, "ShipConfirmed"),
        };

        var repo = Substitute.For<IOrderTransitionRepository>();
        repo.ListByOrderIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(rows);

        var sut = new GetOrderTransitionsHandler(repo);

        var result = await sut.Handle(new GetOrderTransitionsQuery(orderId), CancellationToken.None);

        result.Should().HaveCount(7);
        result.Select(t => t.ToState).Should().Equal(
            "AwaitingReservation", "Reserved", "AwaitingPick", "Picked",
            "AwaitingPack", "Packed", "Shipped");

        // OccurredAt preserved + ASC ordering retained.
        result.Select(t => t.OccurredAt).Should().BeInAscendingOrder();
        result[0].OccurredAt.Should().Be(BaseTime);
        result[6].OccurredAt.Should().Be(BaseTime.AddSeconds(6));

        // Field-level projection: every column mapped.
        var first = result[0];
        first.OrderId.Should().Be(orderId);
        first.FromState.Should().Be("Created");
        first.ToState.Should().Be("AwaitingReservation");
        first.EventType.Should().Be("OrderPlacedV1");
        first.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_NoTransitions_ReturnsEmptyList()
    {
        var orderId = Guid.NewGuid();
        var repo = Substitute.For<IOrderTransitionRepository>();
        repo.ListByOrderIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OrderTransition>());

        var sut = new GetOrderTransitionsHandler(repo);

        var result = await sut.Handle(new GetOrderTransitionsQuery(orderId), CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DelegatesToRepositoryWithMatchingOrderId()
    {
        var repo = Substitute.For<IOrderTransitionRepository>();
        repo.ListByOrderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OrderTransition>());

        var sut = new GetOrderTransitionsHandler(repo);

        var orderId = Guid.NewGuid();
        await sut.Handle(new GetOrderTransitionsQuery(orderId), CancellationToken.None);

        await repo.Received(1).ListByOrderIdAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullRequest_Throws()
    {
        var repo = Substitute.For<IOrderTransitionRepository>();
        var sut = new GetOrderTransitionsHandler(repo);

        Func<Task> act = () => sut.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
