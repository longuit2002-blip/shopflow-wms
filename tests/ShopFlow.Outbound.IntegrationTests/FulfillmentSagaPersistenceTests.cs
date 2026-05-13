using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U4 — saga state persistence against real Postgres.
/// Confirms MassTransit's EF saga repository binds to <c>saga_state</c>,
/// inserts/updates the row on every transition, and the column shape from
/// U1's migration matches MT 8.3.4's expectations (K15 verification).
/// </summary>
/// <remarks>
/// <para>Scope: this test exercises the EF saga repository path against
/// one provisioned tenant DB — single-tenant assertion. The
/// <see cref="SagaPerTenantBindingTests"/> sibling test covers the K12
/// per-tenant DbContext binding gate (two tenants).</para>
///
/// <para>Test wiring: <c>AddMassTransitTestHarness</c> + the in-process
/// transport + an EF saga repository pointing at the test tenant DB.
/// The OutboundDbContext registration is direct-instance bound to the
/// provisioned tenant's options (not the per-request factory) — the K12
/// binding path is covered separately. Here we just confirm "saga writes
/// to saga_state".</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FulfillmentSagaPersistenceTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public FulfillmentSagaPersistenceTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("saga-persist");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly IReadOnlyList<OrderPlacedLineV1> TwoLines = new[]
    {
        new OrderPlacedLineV1(OrderLineId: "L1", Sku: "SKU-A", Qty: 2, ExpectedWeight: 100),
        new OrderPlacedLineV1(OrderLineId: "L2", Sku: "SKU-B", Qty: 5, ExpectedWeight: 50),
    };

    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        // RequestContext bound to the test tenant for the entire scope —
        // OutboundDbContext (registered as Scoped below) resolves
        // IRequestContext.DbConnectionString at construction so it picks
        // the test tenant's connection string.
        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<RequestContext>(rc);
        services.AddSingleton<IRequestContext>(rc);

        services.AddScoped<OutboundDbContext>(_ => new OutboundDbContext(_tenant.Options));

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    r.UsePostgres();
                });
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    [Fact]
    public async Task OrderPlaced_PersistsSagaStateRow_WithAwaitingReservationState()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderId = Guid.NewGuid();
        var tenantId = _tenant.Info.Id;
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: tenantId,
                ChannelExternalOrderId: "ext-persist-1",
                ShippingProfile: "standard",
                Lines: TwoLines,
                OccurredAt: DateTime.UtcNow
            )
        );

        // Wait for the saga to be consumed AND the EF write to land.
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        // The saga write commits when the consume scope disposes — give
        // MT a moment to flush the EF saga repository's SaveChangesAsync.
        await WaitForSagaRowAsync(orderId);

        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "CurrentState", "CorrelationId", tenant_id, shipping_profile, line_count
              FROM saga_state
             WHERE "CorrelationId" = @oid
            """;
        cmd.Parameters.AddWithValue("oid", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("saga_state row must materialize");
        reader.GetString(0).Should().Be("AwaitingReservation");
        reader.GetGuid(1).Should().Be(orderId);
        reader.GetGuid(2).Should().Be(tenantId);
        reader.GetString(3).Should().Be("standard");
        reader.GetInt32(4).Should().Be(2);
    }

    [Fact]
    public async Task OrderPlaced_AssignsCorrelationIdToOrderId()
    {
        // Per K2 — the saga's PK column equals the originating OrderId so
        // every subsequent event can CorrelateById without a side index.
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderId,
                _tenant.Info.Id,
                "ext-corr",
                "express",
                TwoLines,
                DateTime.UtcNow
            )
        );
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();
        await WaitForSagaRowAsync(orderId);

        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM saga_state WHERE "CorrelationId" = @oid""";
        cmd.Parameters.AddWithValue("oid", orderId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1);
    }

    [Fact]
    public async Task SagaStateColumnTypes_MatchExpectedShape()
    {
        // Defensive: a column-type drift (e.g., CurrentState mapped as int
        // instead of text) would surface here as a Postgres conversion
        // error. The MT.EFCore + EF Core 9 K15 verification at U1 is the
        // first-line guard; this test reasserts the column shape after
        // the U4 entity configuration applies.
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderId,
                _tenant.Info.Id,
                "ext-types",
                "standard",
                TwoLines,
                DateTime.UtcNow
            )
        );
        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();
        await WaitForSagaRowAsync(orderId);

        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT data_type
              FROM information_schema.columns
             WHERE table_name = 'saga_state'
               AND column_name = 'CurrentState'
            """;
        var type = (string)(await cmd.ExecuteScalarAsync())!;
        // text or character varying — both are acceptable string columns.
        type.Should().BeOneOf("text", "character varying");
    }

    /// <summary>
    /// MT 8.3.4's saga repo writes the row asynchronously after Consume()
    /// returns; the test harness's <c>Consumed</c> awaiter returns when
    /// the message body was processed, not when the saga commit finalizes.
    /// Poll for the row to appear with a small retry budget rather than
    /// a fixed Task.Delay (more deterministic on slower CI runners).
    /// </summary>
    private async Task WaitForSagaRowAsync(Guid correlationId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT COUNT(*) FROM saga_state WHERE "CorrelationId" = @oid""";
            cmd.Parameters.AddWithValue("oid", correlationId);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            if (count >= 1)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"saga_state row for {correlationId} did not appear within 10s.");
    }
}
