using Microsoft.EntityFrameworkCore;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Repositories;

namespace ShopFlow.Outbound.IntegrationTests.Persistence;

/// <summary>
/// Sprint-7 U1 — <see cref="OrderTransitionRepository"/> against real
/// Postgres. Validates the append-only audit row round-trips correctly,
/// multi-row append preserves chronological order, and per-order
/// isolation holds for the list query.
/// </summary>
/// <remarks>
/// <para>Mirrors <c>OrderRepositoryTests</c>: shares the Testcontainers
/// Postgres lifetime via <c>OutboundTenantFixture</c> + xUnit collection;
/// each test provisions a fresh tenant DB so audit-write ordering is
/// isolated.</para>
///
/// <para>The repository's <c>AppendAsync</c> does not flush; tests call
/// <c>SaveChangesAsync</c> explicitly on the DbContext to commit. In
/// production, the saga's MT EF saga repository commit flushes the audit
/// row alongside the saga state update (Sprint-7 U2 wires the observer).</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrderTransitionRepositoryTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public OrderTransitionRepositoryTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("order-transitions");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AppendAsync_ThenList_RoundTripsTransitionWithAllFields()
    {
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 19, 10, 30, 0, DateTimeKind.Utc);
        var transition = OrderTransition.Create(
            orderId,
            fromState: "AwaitingReservation",
            toState: "Reserved",
            occurredAt: occurredAt,
            eventType: "StockReservedV1",
            correlationId: "00-trace-id-1234-01"
        );

        await using (var dbWrite = new OutboundDbContext(_tenant.Options))
        {
            await new OrderTransitionRepository(dbWrite).AppendAsync(
                transition,
                CancellationToken.None
            );
            await dbWrite.SaveChangesAsync();
        }

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var rows = await new OrderTransitionRepository(dbRead).ListByOrderIdAsync(
            orderId,
            CancellationToken.None
        );

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.OrderId.Should().Be(orderId);
        row.FromState.Should().Be("AwaitingReservation");
        row.ToState.Should().Be("Reserved");
        row.OccurredAt.Should().Be(occurredAt);
        row.EventType.Should().Be("StockReservedV1");
        row.CorrelationId.Should().Be("00-trace-id-1234-01");
    }

    [Fact]
    public async Task ListByOrderIdAsync_MultipleTransitions_ReturnsRowsInOccurredAtAscOrder()
    {
        var orderId = Guid.NewGuid();
        var t0 = new DateTime(2026, 5, 19, 10, 0, 0, DateTimeKind.Utc);

        await using (var dbWrite = new OutboundDbContext(_tenant.Options))
        {
            var repo = new OrderTransitionRepository(dbWrite);
            // Append in non-chronological order — list should still return ASC.
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderId,
                    "AwaitingPick",
                    "Picked",
                    t0.AddSeconds(20),
                    "PickConfirmedV1",
                    "00-trace-id-3"
                ),
                CancellationToken.None
            );
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderId,
                    "Initial",
                    "AwaitingReservation",
                    t0,
                    "OrderPlacedV1",
                    "00-trace-id-1"
                ),
                CancellationToken.None
            );
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderId,
                    "AwaitingReservation",
                    "Reserved",
                    t0.AddSeconds(10),
                    "StockReservedV1",
                    "00-trace-id-2"
                ),
                CancellationToken.None
            );
            await dbWrite.SaveChangesAsync();
        }

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var rows = await new OrderTransitionRepository(dbRead).ListByOrderIdAsync(
            orderId,
            CancellationToken.None
        );

        rows.Should().HaveCount(3);
        rows[0].ToState.Should().Be("AwaitingReservation");
        rows[1].ToState.Should().Be("Reserved");
        rows[2].ToState.Should().Be("Picked");
        rows.Should().BeInAscendingOrder(r => r.OccurredAt);
    }

    [Fact]
    public async Task ListByOrderIdAsync_MultiOrder_IsolatesByOrderId()
    {
        var orderX = Guid.NewGuid();
        var orderY = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;

        await using (var dbWrite = new OutboundDbContext(_tenant.Options))
        {
            var repo = new OrderTransitionRepository(dbWrite);
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderX,
                    "Initial",
                    "AwaitingReservation",
                    occurredAt,
                    "OrderPlacedV1",
                    "trace-x"
                ),
                CancellationToken.None
            );
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderY,
                    "Initial",
                    "AwaitingReservation",
                    occurredAt,
                    "OrderPlacedV1",
                    "trace-y"
                ),
                CancellationToken.None
            );
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderX,
                    "AwaitingReservation",
                    "Reserved",
                    occurredAt.AddSeconds(1),
                    "StockReservedV1",
                    "trace-x"
                ),
                CancellationToken.None
            );
            await dbWrite.SaveChangesAsync();
        }

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var repoRead = new OrderTransitionRepository(dbRead);

        var rowsX = await repoRead.ListByOrderIdAsync(orderX, CancellationToken.None);
        var rowsY = await repoRead.ListByOrderIdAsync(orderY, CancellationToken.None);

        rowsX.Should().HaveCount(2);
        rowsX.Should().AllSatisfy(r => r.OrderId.Should().Be(orderX));
        rowsY.Should().HaveCount(1);
        rowsY[0].OrderId.Should().Be(orderY);
        rowsY[0].CorrelationId.Should().Be("trace-y");
    }

    [Fact]
    public async Task ListByOrderIdAsync_UnknownOrder_ReturnsEmptyList()
    {
        await using var db = new OutboundDbContext(_tenant.Options);
        var rows = await new OrderTransitionRepository(db).ListByOrderIdAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_IdenticalOccurredAt_BothRowsPersistDeterministically()
    {
        // Clock-granularity collision — saga consume processes two
        // transitions within one Postgres clock tick. Both rows must land
        // and ordering by occurred_at is non-deterministic between them
        // (PK breaks the tie at the index level), but both are returned.
        var orderId = Guid.NewGuid();
        var sameMoment = new DateTime(2026, 5, 19, 10, 30, 0, DateTimeKind.Utc);

        await using (var dbWrite = new OutboundDbContext(_tenant.Options))
        {
            var repo = new OrderTransitionRepository(dbWrite);
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderId,
                    "AwaitingPack",
                    "Packed",
                    sameMoment,
                    "PackConfirmedV1",
                    "trace-1"
                ),
                CancellationToken.None
            );
            await repo.AppendAsync(
                OrderTransition.Create(
                    orderId,
                    "Packed",
                    "AwaitingShip",
                    sameMoment,
                    "PackConfirmedV1",
                    "trace-1"
                ),
                CancellationToken.None
            );
            await dbWrite.SaveChangesAsync();
        }

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var rows = await new OrderTransitionRepository(dbRead).ListByOrderIdAsync(
            orderId,
            CancellationToken.None
        );

        rows.Should().HaveCount(2);
        rows.Should().AllSatisfy(r => r.OccurredAt.Should().Be(sameMoment));
    }

    [Fact]
    public async Task MigrateAsync_AddsOutboundSagaTransitionsTable()
    {
        // U1 R14 — the new migration's named table must exist after
        // provisioning. The fixture already calls MigrateAsync() during
        // ProvisionTenantAsync; this test asserts the table is reachable
        // via raw SQL through the same connection string.
        await using var db = new OutboundDbContext(_tenant.Options);
        var exists = await db
            .Database.SqlQueryRaw<bool>(
                @"SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name = 'outbound_saga_transitions'
                  )::boolean"
            )
            .FirstAsync();
        exists.Should().BeTrue();
    }
}
