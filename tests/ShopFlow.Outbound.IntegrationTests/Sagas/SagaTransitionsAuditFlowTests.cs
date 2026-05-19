using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.IntegrationTests.Sagas;

/// <summary>
/// Sprint-7 U2 — end-to-end audit-write flow. Drives the
/// <see cref="FulfillmentSaga"/> through real state transitions on a
/// Testcontainers Postgres tenant DB and asserts the
/// <c>outbound_saga_transitions</c> table accumulates one row per
/// transition. Validates the doc-review architectural decision
/// (comprehensive branch coverage) is wired through the saga's actual
/// DSL: the chained <c>StockReserved → Reserved → AwaitingPick</c>
/// compound transition produces two audit rows.
/// </summary>
/// <remarks>
/// <para>The Sprint-3-redux <c>FulfillmentSagaTests</c> harness builds an
/// in-memory MT TestHarness without the observer; these tests register
/// the full Sprint-7 dependency chain
/// (<see cref="SagaTransitionObserver"/> + repositories + outbox + clock +
/// scoped <see cref="OutboundDbContext"/>) so the saga's
/// <c>RecordTransitionAsync</c> helper resolves and writes through to
/// real Postgres.</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaTransitionsAuditFlowTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public SagaTransitionsAuditFlowTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("saga-audit");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider BuildHarness(out ITestHarness harness)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        // RequestContext bound to the provisioned tenant so the scoped
        // OutboundDbContext + IOutboundOutbox land in the right per-tenant DB.
        var requestContext = _tenant.BuildRequestContext();
        services.AddSingleton<IRequestContext>(requestContext);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(ctx.DbConnectionString)
                .Options;
            return new OutboundDbContext(options);
        });
        services.AddScoped<IOrderTransitionRepository, OrderTransitionRepository>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();
        services.AddScoped<SagaTransitionObserver>();

        // U5 — IPickQueue Singleton: the StockReserved Then handler resolves
        // IPickQueue from the consume scope and writes a PickRequestV1.
        services.AddSingleton<IPickQueue, PickQueueImpl>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .InMemoryRepository();
        });

        var sp = services.BuildServiceProvider(true);
        harness = sp.GetRequiredService<ITestHarness>();
        return sp;
    }

    private async Task<int> CountTransitionsAsync(Guid orderId)
    {
        await using var db = new OutboundDbContext(_tenant.Options);
        return await db.OrderTransitions.CountAsync(t => t.OrderId == orderId);
    }

    private async Task<IReadOnlyList<OrderTransition>> ListTransitionsAsync(Guid orderId)
    {
        await using var db = new OutboundDbContext(_tenant.Options);
        return await db
            .OrderTransitions.Where(t => t.OrderId == orderId)
            .OrderBy(t => t.OccurredAt)
            .ToListAsync();
    }

    [Fact]
    public async Task OrderPlacedV1_WritesInitialToAwaitingReservationAuditRow()
    {
        await using var sp = BuildHarness(out var harness);
        await harness.Start();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: tenantId,
                ChannelExternalOrderId: "ext-audit-1",
                ShippingProfile: "standard",
                Lines: new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
                OccurredAt: DateTime.UtcNow
            )
        );

        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        var rows = await ListTransitionsAsync(orderId);
        rows.Should().HaveCount(1);
        rows[0].FromState.Should().Be("Initial");
        rows[0].ToState.Should().Be("AwaitingReservation");
        rows[0].EventType.Should().Be(nameof(OrderPlacedV1));
    }

    [Fact]
    public async Task StockReservedV1_WritesBothReservedAndAwaitingPickAuditRows()
    {
        // The compound chain StockReserved → Reserved → AwaitingPick must
        // produce TWO audit rows — once at TransitionTo(Reserved) and once
        // at TransitionTo(AwaitingPick). The doc-review decision called this
        // out as a case the per-Then approach must cover.
        await using var sp = BuildHarness(out var harness);
        await harness.Start();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderId,
                tenantId,
                "ext-audit-2",
                "standard",
                new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
                DateTime.UtcNow
            )
        );
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        await harness.Bus.Publish(
            new StockReservedV1(
                OrderId: orderId,
                TenantId: tenantId,
                LineOutcomes: new[] { new StockReservedLineOutcomeV1("L1", "SKU-A", 1, "reserved") },
                OccurredAt: DateTime.UtcNow
            )
        );
        (await harness.Consumed.Any<StockReservedV1>()).Should().BeTrue();

        var rows = await ListTransitionsAsync(orderId);
        rows.Should().HaveCount(3);
        rows[0].ToState.Should().Be("AwaitingReservation");
        rows[1].FromState.Should().Be("AwaitingReservation");
        rows[1].ToState.Should().Be("Reserved");
        rows[2].FromState.Should().Be("Reserved");
        rows[2].ToState.Should().Be("AwaitingPick");
    }

    [Fact]
    public async Task StockReservationFailedV1_WritesPathACompensationToCancelledRows()
    {
        // Path A: atomic-CTE failure → AwaitingReservation → CompensatingReservation
        // → Cancelled (WhenEnter IfElse branch with empty release set short-circuit).
        // The doc-review decision called this WhenEnter-IfElse path out as
        // exactly the kind of branch the per-Then plan would miss.
        await using var sp = BuildHarness(out var harness);
        await harness.Start();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderId,
                tenantId,
                "ext-audit-3",
                "standard",
                new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
                DateTime.UtcNow
            )
        );
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        await harness.Bus.Publish(
            new StockReservationFailedV1(
                OrderId: orderId,
                TenantId: tenantId,
                Reason: "insufficient_stock",
                OccurredAt: DateTime.UtcNow
            )
        );
        (await harness.Consumed.Any<StockReservationFailedV1>()).Should().BeTrue();

        var rows = await ListTransitionsAsync(orderId);
        // Expected: Initial→AwaitingReservation, AwaitingReservation→CompensatingReservation,
        // CompensatingReservation→Cancelled (Path A via WhenEnter IfElse short-circuit).
        rows.Should().HaveCount(3);
        rows[2].FromState.Should().Be("CompensatingReservation");
        rows[2].ToState.Should().Be("Cancelled");
        rows[2].EventType.Should().Be("PathA_EmptyReleaseSet");
    }
}
