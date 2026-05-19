using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.UnitTests.Sagas;

/// <summary>
/// Sprint-7 U2 — <see cref="SagaTransitionObserver"/> in isolation. Verifies
/// the observer writes the audit row + appends the integration event with
/// the correct field mapping, uses the injected <see cref="TimeProvider"/>
/// for occurred-at, and captures <see cref="Activity.Current"/>.Id for
/// correlation. The full state-machine wiring (every TransitionTo site
/// calling the observer) is covered by the integration test
/// <c>SagaTransitionsAuditFlowTests</c>.
/// </summary>
public sealed class SagaTransitionObserverTests
{
    [Fact]
    public async Task RecordAsync_AppendsAuditRowWithAllFields()
    {
        var transitions = Substitute.For<IOrderTransitionRepository>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero));
        var observer = new SagaTransitionObserver(transitions, outbox, clock);

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await observer.RecordAsync(
            orderId: orderId,
            tenantId: tenantId,
            fromState: "AwaitingReservation",
            toState: "Reserved",
            eventType: nameof(SagaTransitionedV1),
            ct: CancellationToken.None
        );

        await transitions
            .Received(1)
            .AppendAsync(
                Arg.Is<OrderTransition>(t =>
                    t.OrderId == orderId
                    && t.FromState == "AwaitingReservation"
                    && t.ToState == "Reserved"
                    && t.OccurredAt == new DateTime(2026, 5, 19, 14, 0, 0, DateTimeKind.Utc)
                    && t.EventType == nameof(SagaTransitionedV1)
                    && !string.IsNullOrWhiteSpace(t.CorrelationId)
                ),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task RecordAsync_AppendsIntegrationEventToOutbox()
    {
        var transitions = Substitute.For<IOrderTransitionRepository>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero));
        var observer = new SagaTransitionObserver(transitions, outbox, clock);

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await observer.RecordAsync(
            orderId,
            tenantId,
            "Picked",
            "Packed",
            "PackConfirmed",
            CancellationToken.None
        );

        await outbox
            .Received(1)
            .AppendAsync(
                nameof(SagaTransitionedV1),
                Arg.Is<SagaTransitionedV1>(e =>
                    e.TenantId == tenantId
                    && e.OrderId == orderId
                    && e.FromState == "Picked"
                    && e.ToState == "Packed"
                    && e.EventType == "PackConfirmed"
                    && e.OccurredAt == new DateTime(2026, 5, 19, 14, 0, 0, DateTimeKind.Utc)
                    && !string.IsNullOrWhiteSpace(e.CorrelationId)
                ),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task RecordAsync_AuditRowAndOutboxEventShareSameCorrelationId()
    {
        var transitions = Substitute.For<IOrderTransitionRepository>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var clock = new FakeTimeProvider();
        var observer = new SagaTransitionObserver(transitions, outbox, clock);

        OrderTransition? capturedTransition = null;
        SagaTransitionedV1? capturedEvent = null;

        transitions
            .When(r => r.AppendAsync(Arg.Any<OrderTransition>(), Arg.Any<CancellationToken>()))
            .Do(ci => capturedTransition = ci.Arg<OrderTransition>());
        outbox
            .When(o => o.AppendAsync(Arg.Any<string>(), Arg.Any<SagaTransitionedV1>(), Arg.Any<CancellationToken>()))
            .Do(ci => capturedEvent = ci.ArgAt<SagaTransitionedV1>(1));

        await observer.RecordAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AwaitingPick",
            "Picked",
            "PickConfirmed",
            CancellationToken.None
        );

        capturedTransition.Should().NotBeNull();
        capturedEvent.Should().NotBeNull();
        capturedTransition!.CorrelationId.Should().Be(capturedEvent!.CorrelationId);
    }

    [Fact]
    public async Task RecordAsync_UsesActivityCurrentIdWhenPresent()
    {
        var transitions = Substitute.For<IOrderTransitionRepository>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var observer = new SagaTransitionObserver(transitions, outbox, new FakeTimeProvider());

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        using var src = new ActivitySource("test");
        using var activity = src.StartActivity("saga-test");
        activity.Should().NotBeNull("ActivitySource must produce an activity for this test");
        var expectedId = activity!.Id;

        await observer.RecordAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Initial",
            "AwaitingReservation",
            "OrderPlacedV1",
            CancellationToken.None
        );

        await transitions
            .Received(1)
            .AppendAsync(
                Arg.Is<OrderTransition>(t => t.CorrelationId == expectedId),
                Arg.Any<CancellationToken>()
            );
    }
}
