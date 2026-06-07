using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Contracts.Inbound;
using ShopFlow.Inbound.Application.Services;
using ShopFlow.Inbound.Domain;
using ShopFlow.Inbound.Infrastructure;
using ShopFlow.Inbound.Infrastructure.Outbox;
using ShopFlow.Inbound.Infrastructure.Repositories;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Consumers;
using ShopFlow.SharedKernel.Application;
using Testcontainers.PostgreSql;

namespace ShopFlow.Inbound.IntegrationTests;

/// <summary>
/// Sprint-2.5 U3 — end-to-end cross-module flow test. Exercises the
/// full Inbound API → ConfirmReceivingLineService → Inbound outbox row →
/// MassTransit publish → InboundConfirmedConsumer → Inventory tenant DB
/// stock change against **a single physical tenant Postgres DB** hosting
/// both modules' schemas (the realistic production shape per ADR-0003).
/// </summary>
/// <remarks>
/// This was the U9 test deferred from Sprint-2-redux. Sprint-2.5 U1+U2
/// closed the outbox-table-name collision (inbound_outbox_messages +
/// inventory_outbox_messages); this U3 unit proves the contract JSON
/// serialization round-trip + dispatcher → consumer hand-off + DB-level
/// cross-module side-effects all hold under that shared-DB shape.
///
/// MassTransit transport is in-memory (test harness). Real RabbitMQ
/// Testcontainers + the dispatcher's actual poll loop are covered by CI
/// on production-shape hardware (Sprint-1-redux already validated the
/// dispatcher's outbox-read + publish loop end-to-end for
/// StockReservedEvent; this test substitutes a synchronous read-and-
/// publish that exercises the same JSON contract path).
/// </remarks>
[Trait("Category", "Integration")]
public sealed class InboundToInventoryFlowTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _adminConn = string.Empty;
    private string _tenantConn = string.Empty;
    private string _dbName = string.Empty;
    private Guid _tenantId;
    private long _binId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _adminConn = _container.GetConnectionString();
        _dbName = $"shopflow_t_xmod_{Guid.NewGuid().ToString("N")[..8]}";

        await using (var admin = new NpgsqlConnection(_adminConn))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        _tenantConn = new NpgsqlConnectionStringBuilder(_adminConn)
        {
            Database = _dbName,
        }.ConnectionString;

        // Apply BOTH modules' migrations to the same physical DB — this
        // was the Sprint-2-redux U9 failure mode (outbox_messages name
        // collision). Sprint-2.5's per-module prefix lets them coexist.
        var inboundOptions = new DbContextOptionsBuilder<InboundDbContext>()
            .UseNpgsql(
                _tenantConn,
                npg => npg.MigrationsAssembly("ShopFlow.Inbound.Infrastructure")
            )
            .Options;
        await using (var ctx = new InboundDbContext(inboundOptions))
        {
            await ctx.Database.MigrateAsync();
        }

        var inventoryOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(
                _tenantConn,
                npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure")
            )
            .Options;
        await using (var ctx = new InventoryDbContext(inventoryOptions))
        {
            await ctx.Database.MigrateAsync();
        }

        _tenantId = Guid.NewGuid();
        _binId = await SeedBinAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task<long> SeedBinAsync()
    {
        await using var conn = new NpgsqlConnection(_tenantConn);
        await conn.OpenAsync();
        await using var zoneCmd = conn.CreateCommand();
        zoneCmd.CommandText =
            "INSERT INTO zones (name, warehouse_id) VALUES ('Z1', 'wh-1') RETURNING zone_id;";
        var zoneId = (long)(await zoneCmd.ExecuteScalarAsync())!;
        await using var binCmd = conn.CreateCommand();
        binCmd.CommandText =
            "INSERT INTO bins (zone_id, name, capacity, occupancy_qty) VALUES (@z, 'B1', 100, 0) RETURNING bin_id;";
        binCmd.Parameters.AddWithValue("z", zoneId);
        return (long)(await binCmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task ReceivingConfirmation_PropagatesToInventoryStock()
    {
        var inboundOpts = new DbContextOptionsBuilder<InboundDbContext>()
            .UseNpgsql(_tenantConn)
            .Options;
        var inventoryOpts = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_tenantConn)
            .Options;

        // ── Inbound side: create PO, open, confirm a line ──────────────
        await using var inboundDb = new InboundDbContext(inboundOpts);
        var po = PurchaseOrder.Create("SUP-1", Now.AddDays(3), new[] { ("SKU-FLOW", 50) }).Value!;
        po.Open(Now);
        await new PurchaseOrderRepository(inboundDb).AddAsync(po, CancellationToken.None);
        await inboundDb.SaveChangesAsync();

        var inboundRc = BuildRequestContext();
        var service = new ConfirmReceivingLineService(
            poRepo: new PurchaseOrderRepository(inboundDb),
            receivingRepo: new ReceivingRepository(inboundDb),
            ticketRepo: new ReconciliationTicketRepository(inboundDb),
            outbox: new InboundOutbox(inboundDb, inboundRc),
            requestContext: inboundRc,
            uow: new InboundUnitOfWork(inboundDb),
            clock: new FakeClock(Now)
        );
        var confirmResult = await service.ConfirmAsync(
            purchaseOrderId: po.Id,
            receivingId: null,
            purchaseOrderLineId: po.Lines.Single().Id,
            actualQty: 50,
            suggestedBinId: _binId,
            actualBinId: _binId,
            operatorId: null,
            ct: CancellationToken.None
        );
        confirmResult.IsSuccess.Should().BeTrue();
        confirmResult.Value!.TicketCreated.Should().BeFalse(); // exact match, no ticket

        // ── Inbound's outbox row now exists in inbound_outbox_messages.
        //    Drain it and publish through MassTransit (substitutes for the
        //    multiplexed dispatcher's poll loop — Sprint-1-redux validates
        //    the dispatcher loop end-to-end for StockReservedEvent).
        var outboxRow = await inboundDb
            .OutboxMessages.AsNoTracking()
            .FirstAsync(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Inbound.InboundConfirmedV1")
            );

        var eventType =
            Type.GetType(outboxRow.EventType, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Outbox row references unknown type: {outboxRow.EventType}"
            );
        var payload =
            JsonSerializer.Deserialize(
                outboxRow.Payload,
                eventType,
                ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default
            )
            ?? throw new InvalidOperationException(
                $"Outbox payload deserialised to null for type {eventType}"
            );

        // ── Inventory side: start consumer host, publish, wait for consume.
        await using var inventoryDb = new InventoryDbContext(inventoryOpts);
        await using var sp = BuildInventoryHost();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(
            payload,
            eventType,
            ctx => ctx.Headers.Set("tenant_id", _tenantId.ToString())
        );

        var consumerHarness = harness.GetConsumerHarness<InboundConfirmedConsumer>();
        var consumed = await consumerHarness.Consumed.Any<InboundConfirmedV1>();
        consumed.Should().BeTrue();

        // Fail fast if the consumer faulted — opaque "no stock row" assertion
        // below would mask the real cause otherwise.
        var faulted = consumerHarness
            .Consumed.Select<InboundConfirmedV1>()
            .Where(c => c.Exception is not null)
            .ToList();
        faulted
            .Should()
            .BeEmpty(
                "consumer must not fault — first fault: "
                    + (faulted.FirstOrDefault()?.Exception?.ToString() ?? "<none>")
            );

        // ── Assert Inventory stock was applied to the shared tenant DB.
        await using var verify = new InventoryDbContext(inventoryOpts);
        var stockRows = await verify.StockItems.AsNoTracking().ToListAsync();
        var stockRow = stockRows.Single(s => s.Sku.Value == "SKU-FLOW");
        stockRow.Available.Value.Should().Be(50);

        var binRow = await verify.StockItemBins.FirstAsync(b => b.Sku == "SKU-FLOW");
        binRow.Quantity.Should().Be(50);
        binRow.BinId.Should().Be(_binId);

        var dedupCount = await verify.InboundDedup.CountAsync();
        dedupCount.Should().Be(1);

        // ── Outbox row was marked processed (Sprint-1-redux pattern) is
        //    not applicable here because we bypassed the dispatcher's
        //    ProcessedAt stamping path. The dispatcher contract is already
        //    validated end-to-end by Sprint-1-redux for StockReservedEvent;
        //    this test focuses on the contract JSON + consumer logic +
        //    physical-DB-shared correctness.
    }

    [Fact]
    public async Task ReceivingMismatch_PropagatesActualStock_TicketLandsInInbound()
    {
        var inboundOpts = new DbContextOptionsBuilder<InboundDbContext>()
            .UseNpgsql(_tenantConn)
            .Options;
        var inventoryOpts = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_tenantConn)
            .Options;

        await using var inboundDb = new InboundDbContext(inboundOpts);
        var po = PurchaseOrder
            .Create("SUP-2", Now.AddDays(3), new[] { ("SKU-MISMATCH", 100) })
            .Value!;
        po.Open(Now);
        await new PurchaseOrderRepository(inboundDb).AddAsync(po, CancellationToken.None);
        await inboundDb.SaveChangesAsync();

        var inboundRc = BuildRequestContext();
        var service = new ConfirmReceivingLineService(
            poRepo: new PurchaseOrderRepository(inboundDb),
            receivingRepo: new ReceivingRepository(inboundDb),
            ticketRepo: new ReconciliationTicketRepository(inboundDb),
            outbox: new InboundOutbox(inboundDb, inboundRc),
            requestContext: inboundRc,
            uow: new InboundUnitOfWork(inboundDb),
            clock: new FakeClock(Now)
        );

        var confirmResult = await service.ConfirmAsync(
            purchaseOrderId: po.Id,
            receivingId: null,
            purchaseOrderLineId: po.Lines.Single().Id,
            actualQty: 95, // under-receipt
            suggestedBinId: _binId,
            actualBinId: _binId,
            operatorId: null,
            ct: CancellationToken.None
        );
        confirmResult.IsSuccess.Should().BeTrue();
        confirmResult.Value!.TicketCreated.Should().BeTrue();

        // Reconciliation ticket lives in Inbound's schema in the same DB.
        await using var ticketCheck = new InboundDbContext(inboundOpts);
        var ticket = await ticketCheck.ReconciliationTickets.FirstAsync();
        ticket.ExpectedQty.Should().Be(100);
        ticket.ActualQty.Should().Be(95);

        // Drain outbox + publish.
        var outboxRow = await inboundDb
            .OutboxMessages.AsNoTracking()
            .FirstAsync(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Inbound.InboundConfirmedV1")
            );
        var eventType = Type.GetType(outboxRow.EventType, throwOnError: true)!;
        var payload = JsonSerializer.Deserialize(
            outboxRow.Payload,
            eventType,
            ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default
        )!;

        await using var sp = BuildInventoryHost();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        await harness.Bus.Publish(
            payload,
            eventType,
            ctx => ctx.Headers.Set("tenant_id", _tenantId.ToString())
        );

        await harness
            .GetConsumerHarness<InboundConfirmedConsumer>()
            .Consumed.Any<InboundConfirmedV1>();

        // Inventory stock reflects ACTUAL qty (95), not expected (100) —
        // the discrepancy is captured in the ticket, the stock change is
        // what physically arrived.
        await using var verify = new InventoryDbContext(inventoryOpts);
        var stockRows = await verify.StockItems.AsNoTracking().ToListAsync();
        var stockRow = stockRows.Single(s => s.Sku.Value == "SKU-MISMATCH");
        stockRow.Available.Value.Should().Be(95);
    }

    private RequestContext BuildRequestContext()
    {
        var info = new ShopFlow.SharedKernel.Application.Ports.TenantInfo(
            Id: _tenantId,
            Slug: "xmod",
            DbName: _dbName,
            DbConnectionString: _tenantConn,
            Region: "ap-southeast-1",
            Tier: "free",
            Status: ShopFlow.SharedKernel.Domain.TenantStatus.Ready
        );
        var rc = new RequestContext();
        rc.Bind(info, Guid.NewGuid().ToString("N"), userId: null);
        return rc;
    }

    private ServiceProvider BuildInventoryHost()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = BuildRequestContext();
        services.AddSingleton<RequestContext>(rc);
        services.AddSingleton<IRequestContext>(rc);

        services.AddScoped<InventoryDbContext>(_ => new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(_tenantConn).Options
        ));
        services.AddScoped<
            IStockItemRepository,
            ShopFlow.Inventory.Infrastructure.Repositories.StockItemRepository
        >();
        services.AddScoped<
            IInboundDedupRepository,
            ShopFlow.Inventory.Infrastructure.Repositories.InboundDedupRepository
        >();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<InboundConfirmedConsumer>();
        });

        return services.BuildServiceProvider(true);
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeClock(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
