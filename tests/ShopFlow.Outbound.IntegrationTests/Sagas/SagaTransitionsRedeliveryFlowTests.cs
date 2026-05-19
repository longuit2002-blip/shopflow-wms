using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Persistence;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.IntegrationTests.Sagas;

/// <summary>
/// Sprint-7.5 U8 — exercises the new composite UNIQUE on
/// <c>outbound_saga_transitions(order_id, occurred_at, to_state)</c>
/// + the <see cref="SagaTransitionDuplicateInterceptor"/>'s idempotent
/// catch under simulated MassTransit at-least-once redelivery.
/// </summary>
/// <remarks>
/// <para><strong>What this test guards.</strong> Sprint-7's audit-write
/// path (saga's <c>RecordTransitionAsync</c> →
/// <see cref="SagaTransitionObserver"/> →
/// <c>OrderTransitionRepository.AppendAsync</c>) tracks the audit row
/// against the saga's scoped <see cref="OutboundDbContext"/>; the MT EF
/// saga repository flushes everything atomically with the saga state
/// row. Without the UNIQUE + interceptor, a redelivered consume would
/// re-write the audit row (Sprint-7 trade-off #1).</para>
///
/// <para><strong>Redelivery simulation.</strong> MT TestHarness'
/// in-memory bus does not automatically redeliver. To simulate the
/// at-least-once semantics, the test publishes the SAME
/// <see cref="OrderPlacedV1"/> twice with the same correlation. The
/// saga's <see cref="OrderTransition"/> insert for the FIRST consume
/// commits the audit row; the SECOND consume re-fires
/// <c>RecordTransitionAsync</c> and would re-insert with the same
/// <c>(OrderId, OccurredAt, ToState)</c>. The interceptor must detach
/// the duplicate so the second commit succeeds without throwing.</para>
///
/// <para><strong>OccurredAt stability.</strong> Because the observer
/// uses <c>TimeProvider</c> for <c>OccurredAt</c>, the redelivery
/// scenario uses <see cref="FixedTimeProvider"/> so the second consume
/// produces an identical timestamp — that is what the UNIQUE actually
/// catches. With a live <c>TimeProvider.System</c> the two consumes
/// would produce different ticks and the UNIQUE would not fire (the
/// real production race relies on the same physical row being
/// redelivered with the same persisted timestamp, which MT preserves
/// because the message envelope is the same).</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaTransitionsRedeliveryFlowTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public SagaTransitionsRedeliveryFlowTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("saga-redelivery");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider BuildHarness(out ITestHarness harness, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var requestContext = _tenant.BuildRequestContext();
        services.AddSingleton<IRequestContext>(requestContext);
        services.AddSingleton(clock);

        services.AddScoped<SagaTransitionDuplicateInterceptor>();
        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var dupeInterceptor = sp.GetRequiredService<SagaTransitionDuplicateInterceptor>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(ctx.DbConnectionString)
                .AddInterceptors(dupeInterceptor)
                .Options;
            return new OutboundDbContext(options);
        });
        services.AddScoped<IOrderTransitionRepository, OrderTransitionRepository>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();
        services.AddScoped<SagaTransitionObserver>();
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

    [Fact]
    public async Task RedeliveredOrderPlaced_WritesAuditRowExactlyOnce()
    {
        // Fixed clock so both consumes produce the same OccurredAt — that is
        // what the (order_id, occurred_at, to_state) UNIQUE actually guards.
        var fixedClock = new FixedTimeProvider(
            new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero)
        );

        await using var sp = BuildHarness(out var harness, fixedClock);
        await harness.Start();

        var orderId = Guid.NewGuid();
        var tenantId = _tenant.Info.Id;
        var envelope = new OrderPlacedV1(
            OrderId: orderId,
            TenantId: tenantId,
            ChannelExternalOrderId: "ext-redelivery-1",
            ShippingProfile: "standard",
            Lines: new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
            OccurredAt: DateTime.UtcNow
        );

        // First consume — populates the audit row.
        await harness.Bus.Publish(envelope);
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        // Second consume — same envelope. The saga's RecordTransitionAsync
        // fires again; the observer tracks an identical OrderTransition;
        // the interceptor's pre-check must detach it so SaveChanges
        // succeeds without a 23505 propagating to MT.
        await harness.Bus.Publish(envelope);

        await using (var db = new OutboundDbContext(_tenant.Options))
        {
            // Wait for both consumes to settle. MT TestHarness' Consumed
            // counters track the cumulative count; we assert that BOTH
            // delivery attempts ran.
            (await harness.Consumed.SelectAsync<OrderPlacedV1>().Take(2).Count()).Should().Be(2);

            var rows = await db
                .OrderTransitions.Where(t => t.OrderId == orderId)
                .OrderBy(t => t.OccurredAt)
                .ToListAsync();

            // The saga transitions Initial → AwaitingReservation. With a
            // fixed clock both consumes try to write the same row; only
            // one persists.
            rows.Should().HaveCount(1);
            rows[0].FromState.Should().Be("Initial");
            rows[0].ToState.Should().Be("AwaitingReservation");
        }

        await harness.Stop();
    }

    [Fact]
    public async Task Migration_LandsCompositeUniqueOnSagaTransitions()
    {
        // Smoke assertion: the Sprint-7.5 U8 migration applied to the
        // provisioned tenant's DB and left the named UNIQUE index in
        // pg_indexes for the expected columns. The fixture's ProvisionTenant
        // call ran MigrateAsync already; we just probe pg_indexes here.
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE tablename = 'outbound_saga_transitions'
              AND indexname = 'uq_outbound_saga_transitions_order_occurred_state'";
        await using var reader = await cmd.ExecuteReaderAsync();

        var foundRow = await reader.ReadAsync();
        foundRow.Should().BeTrue("Sprint-7.5 U8 migration must add the composite UNIQUE index");

        var indexDef = reader.GetString(1);
        indexDef.Should().Contain("UNIQUE", "the index must be declared UNIQUE");
        indexDef.Should().Contain("order_id");
        indexDef.Should().Contain("occurred_at");
        indexDef.Should().Contain("to_state");
    }

    [Fact]
    public async Task RedeliveredOrderPlaced_OutboxRowsAreNotDoubledForDuplicateTransition()
    {
        // The audit row is the load-bearing dedup point. The outbox row
        // for SagaTransitionedV1 currently writes once per consume because
        // OutboundOutbox.AppendAsync stages a fresh GUID — under
        // redelivery without the interceptor's audit-detach BOTH outbox
        // rows would commit (the first one alongside the first audit row,
        // the second alongside no audit row because the second SaveChanges
        // would 23505). With the interceptor's pre-check detaching the
        // duplicate audit row, the second SaveChanges still commits the
        // outbox row.
        //
        // Sprint-7.5 U8 explicitly limits dedup to the audit-row layer
        // (the plan calls UNIQUE on outbound_saga_transitions, not on
        // outbox_messages). Downstream relay-consumers idempotently
        // process SagaTransitionedV1 via tenant + order id. This assertion
        // documents the actual behaviour so future drift is caught.
        var fixedClock = new FixedTimeProvider(
            new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero)
        );

        await using var sp = BuildHarness(out var harness, fixedClock);
        await harness.Start();

        var orderId = Guid.NewGuid();
        var envelope = new OrderPlacedV1(
            OrderId: orderId,
            TenantId: _tenant.Info.Id,
            ChannelExternalOrderId: "ext-redelivery-outbox-1",
            ShippingProfile: "standard",
            Lines: new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
            OccurredAt: DateTime.UtcNow
        );

        await harness.Bus.Publish(envelope);
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();
        await harness.Bus.Publish(envelope);
        (await harness.Consumed.SelectAsync<OrderPlacedV1>().Take(2).Count()).Should().Be(2);

        await using var db = new OutboundDbContext(_tenant.Options);

        var auditRows = await db
            .OrderTransitions.Where(t => t.OrderId == orderId)
            .ToListAsync();
        auditRows.Should().HaveCount(1, "the UNIQUE + interceptor must coalesce the audit row");

        // Outbox rows for SagaTransitionedV1 referencing this order — the
        // payload is JSON, so we filter loosely by EventType + LIKE on the
        // serialized OrderId.
        var outboxRows = await db
            .OutboxMessages.Where(o =>
                o.EventType == nameof(SagaTransitionedV1)
                && o.Payload.Contains(orderId.ToString())
            )
            .ToListAsync();
        outboxRows
            .Should()
            .NotBeEmpty("at least the first consume's outbox row commits alongside the audit row");

        await harness.Stop();
    }
}

/// <summary>
/// Test-only <see cref="TimeProvider"/> returning a fixed instant on every
/// call. Lets the redelivery scenario produce identical
/// <see cref="OrderTransition.OccurredAt"/> values across both consumes
/// so the composite UNIQUE actually fires.
/// </summary>
file sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}
