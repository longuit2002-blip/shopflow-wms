using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Persistence;

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
            .When(o =>
                o.AppendAsync(
                    Arg.Any<string>(),
                    Arg.Any<SagaTransitionedV1>(),
                    Arg.Any<CancellationToken>()
                )
            )
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
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
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

    // ─────────────────────────────────────────────────────────────────────
    // Sprint-7.5 U8 — SagaTransitionDuplicateInterceptor
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-check path: when an OrderTransition is added whose
    /// <c>(OrderId, OccurredAt, ToState)</c> triple already exists in the
    /// DbSet, <see cref="SagaTransitionDuplicateInterceptor.SavingChangesAsync"/>
    /// detaches the entity before SaveChanges executes — the
    /// would-be 23505 never fires and the commit succeeds.
    /// </summary>
    [Fact]
    public async Task Interceptor_DetachesAddedTransitionThatAlreadyExists()
    {
        var interceptor = new SagaTransitionDuplicateInterceptor(
            NullLogger<SagaTransitionDuplicateInterceptor>.Instance
        );

        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseInMemoryDatabase($"interceptor-dup-{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var orderId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 19, 14, 0, 0, DateTimeKind.Utc);
        const string toState = "Reserved";

        // First save — populate the row.
        await using (var db = new OutboundDbContext(options))
        {
            db.OrderTransitions.Add(
                OrderTransition.Create(
                    orderId: orderId,
                    fromState: "AwaitingReservation",
                    toState: toState,
                    occurredAt: occurredAt,
                    eventType: nameof(SagaTransitionedV1),
                    correlationId: "trace-1"
                )
            );
            await db.SaveChangesAsync();
        }

        // Second save — same triple. Interceptor must detach so the
        // duplicate INSERT never goes through. SaveChanges must NOT throw
        // and the row count must stay at 1.
        await using (var db = new OutboundDbContext(options))
        {
            db.OrderTransitions.Add(
                OrderTransition.Create(
                    orderId: orderId,
                    fromState: "AwaitingReservation",
                    toState: toState,
                    occurredAt: occurredAt,
                    eventType: nameof(SagaTransitionedV1),
                    correlationId: "trace-2"
                )
            );
            var affected = await db.SaveChangesAsync();
            affected.Should().Be(0, "the duplicate audit row should have been detached");
        }

        await using (var db = new OutboundDbContext(options))
        {
            var rows = await db.OrderTransitions.Where(t => t.OrderId == orderId).ToListAsync();
            rows.Should().HaveCount(1);
        }
    }

    /// <summary>
    /// Pre-check path with mixed adds: a non-duplicate transition for a
    /// different triple must still be persisted; only the duplicate is
    /// detached.
    /// </summary>
    [Fact]
    public async Task Interceptor_PreservesNonDuplicateAddsAlongsideDetached()
    {
        var interceptor = new SagaTransitionDuplicateInterceptor(
            NullLogger<SagaTransitionDuplicateInterceptor>.Instance
        );

        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseInMemoryDatabase($"interceptor-mixed-{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var orderId = Guid.NewGuid();
        var dupOccurredAt = new DateTime(2026, 5, 19, 14, 0, 0, DateTimeKind.Utc);
        var freshOccurredAt = new DateTime(2026, 5, 19, 14, 0, 1, DateTimeKind.Utc);

        await using (var db = new OutboundDbContext(options))
        {
            db.OrderTransitions.Add(
                OrderTransition.Create(
                    orderId,
                    "AwaitingReservation",
                    "Reserved",
                    dupOccurredAt,
                    nameof(SagaTransitionedV1),
                    "trace-1"
                )
            );
            await db.SaveChangesAsync();
        }

        await using (var db = new OutboundDbContext(options))
        {
            // Duplicate — will be detached.
            db.OrderTransitions.Add(
                OrderTransition.Create(
                    orderId,
                    "AwaitingReservation",
                    "Reserved",
                    dupOccurredAt,
                    nameof(SagaTransitionedV1),
                    "trace-dup"
                )
            );
            // Fresh — must be persisted.
            db.OrderTransitions.Add(
                OrderTransition.Create(
                    orderId,
                    "Reserved",
                    "AwaitingPick",
                    freshOccurredAt,
                    nameof(SagaTransitionedV1),
                    "trace-fresh"
                )
            );
            var affected = await db.SaveChangesAsync();
            affected.Should().Be(1, "one fresh transition should persist; the dup is detached");
        }

        await using (var db = new OutboundDbContext(options))
        {
            var rows = await db
                .OrderTransitions.Where(t => t.OrderId == orderId)
                .OrderBy(t => t.OccurredAt)
                .ToListAsync();
            rows.Should().HaveCount(2);
            rows[0].ToState.Should().Be("Reserved");
            rows[1].ToState.Should().Be("AwaitingPick");
        }
    }

    /// <summary>
    /// SqlState + ConstraintName classifier: <c>23505</c> against the
    /// saga-transitions UNIQUE constraint is recognised as a
    /// duplicate-violation that should be swallowed.
    /// </summary>
    [Fact]
    public void Classify_TrueForMatching23505AndConstraintName()
    {
        SagaTransitionDuplicateInterceptor
            .Classify(
                PostgresErrorCodes.UniqueViolation,
                SagaTransitionDuplicateInterceptor.UniqueConstraintName
            )
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// SqlState + ConstraintName classifier: <c>23502</c> (not-null
    /// violation) is NOT recognised — guards against the interceptor
    /// swallowing genuine bugs even when the constraint name happens to
    /// match.
    /// </summary>
    [Fact]
    public void Classify_FalseForNonUniqueSqlState()
    {
        SagaTransitionDuplicateInterceptor
            .Classify("23502", SagaTransitionDuplicateInterceptor.UniqueConstraintName)
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// SqlState + ConstraintName classifier: <c>23505</c> against a
    /// different UNIQUE constraint (e.g. the orders.channel_external_order_id
    /// idempotency anchor) is NOT recognised — only the saga-transitions
    /// constraint name triggers swallow.
    /// </summary>
    [Fact]
    public void Classify_FalseForDifferentConstraintName()
    {
        SagaTransitionDuplicateInterceptor
            .Classify(PostgresErrorCodes.UniqueViolation, "ux_orders_channel_external_order_id")
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// SqlState + ConstraintName classifier: null inputs (no Postgres
    /// error info available) are NOT recognised.
    /// </summary>
    [Fact]
    public void Classify_FalseForNullInputs()
    {
        SagaTransitionDuplicateInterceptor
            .Classify(sqlState: null, constraintName: null)
            .Should()
            .BeFalse();
        SagaTransitionDuplicateInterceptor
            .Classify(sqlState: PostgresErrorCodes.UniqueViolation, constraintName: null)
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// Exception classifier: a non-Postgres exception inner is NOT
    /// recognised.
    /// </summary>
    [Fact]
    public void IsDuplicateSagaTransitionViolation_FalseForNonPostgresInner()
    {
        var dbUpdate = new DbUpdateException(
            "Save failed",
            new InvalidOperationException("not a postgres error")
        );

        SagaTransitionDuplicateInterceptor
            .IsDuplicateSagaTransitionViolation(dbUpdate)
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// Exception classifier: a non-DbUpdateException is NOT recognised.
    /// </summary>
    [Fact]
    public void IsDuplicateSagaTransitionViolation_FalseForNonDbUpdateException()
    {
        SagaTransitionDuplicateInterceptor
            .IsDuplicateSagaTransitionViolation(new InvalidOperationException("nope"))
            .Should()
            .BeFalse();
        SagaTransitionDuplicateInterceptor
            .IsDuplicateSagaTransitionViolation(null)
            .Should()
            .BeFalse();
    }
}
