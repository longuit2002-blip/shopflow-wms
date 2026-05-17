using Microsoft.EntityFrameworkCore;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Domain.Aggregates;
using ShopFlow.StockSync.Infrastructure;
using ShopFlow.StockSync.Infrastructure.Persistence.Repositories;

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U5 / R12 — <c>stock_sync_push_log</c> persistence
/// against Testcontainers Postgres. Covers the three terminal states
/// (Success / Failed / BreakerOpen) and the UNIQUE-23505 idempotency
/// catch on
/// <c>ux_stock_sync_push_log_idempotency</c>.
/// </summary>
/// <remarks>
/// Mirrors the Sprint-1-redux <c>ReservationRepository</c> test
/// shape: one tenant DB per test (provisioned through the shared
/// fixture), real EF Core <see cref="DbContext"/> directly against
/// the migrated schema, no harness layering.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(StockSyncTenantCollection.Name)]
public sealed class PushLogPersistenceTests
{
    private readonly StockSyncTenantFixture _fixture;

    public PushLogPersistenceTests(StockSyncTenantFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly DateTime ObservedAt = new(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PushedAt = new(2026, 5, 17, 9, 0, 1, DateTimeKind.Utc);

    private static string KeyFor(Guid tenantId, string sku, string channel) =>
        PushIntent.BuildIdempotencyKey(tenantId, sku, channel, ObservedAt);

    [Fact]
    public async Task AppendAsync_MarkSucceeded_PersistsOneRow()
    {
        var tenant = await _fixture.ProvisionTenantAsync("psl-succeed");
        await using var db = new StockSyncDbContext(tenant.Options);
        var repo = new PushLogRepository(db);

        var entry = PushLogEntry.MarkSucceeded(
            tenantId: tenant.Info.Id,
            channelType: "shopee",
            sku: "SKU-1",
            available: 42,
            idempotencyKey: KeyFor(tenant.Info.Id, "SKU-1", "shopee"),
            latencyMs: 25,
            observedAt: ObservedAt,
            pushedAt: PushedAt
        );

        await repo.AppendAsync(entry, CancellationToken.None);

        await using var verify = new StockSyncDbContext(tenant.Options);
        var rows = await verify.PushLogEntries.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be("Success");
        rows[0].LatencyMs.Should().Be(25);
        rows[0].ErrorCode.Should().BeNull();
        rows[0].Available.Should().Be(42);
    }

    [Fact]
    public async Task AppendAsync_DuplicateIdempotencyKey_LeavesOneRow()
    {
        var tenant = await _fixture.ProvisionTenantAsync("psl-dup");
        var key = KeyFor(tenant.Info.Id, "SKU-2", "shopee");

        await using (var db1 = new StockSyncDbContext(tenant.Options))
        {
            var repo1 = new PushLogRepository(db1);
            await repo1.AppendAsync(
                PushLogEntry.MarkSucceeded(
                    tenant.Info.Id, "shopee", "SKU-2", 7, key,
                    latencyMs: 20,
                    observedAt: ObservedAt,
                    pushedAt: PushedAt
                ),
                CancellationToken.None
            );
        }

        await using (var db2 = new StockSyncDbContext(tenant.Options))
        {
            var repo2 = new PushLogRepository(db2);
            // Same idempotency_key — even though the row carries a
            // different latency, the 23505 catch drops the second
            // insert silently and the first row stays canonical.
            await repo2.AppendAsync(
                PushLogEntry.MarkSucceeded(
                    tenant.Info.Id, "shopee", "SKU-2", 7, key,
                    latencyMs: 999,
                    observedAt: ObservedAt,
                    pushedAt: PushedAt
                ),
                CancellationToken.None
            );
        }

        await using var verify = new StockSyncDbContext(tenant.Options);
        var rows = await verify.PushLogEntries.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].LatencyMs.Should().Be(20);
    }

    [Fact]
    public async Task AppendAsync_MarkFailed_PersistsWithErrorCode()
    {
        var tenant = await _fixture.ProvisionTenantAsync("psl-failed");
        await using var db = new StockSyncDbContext(tenant.Options);
        var repo = new PushLogRepository(db);

        var entry = PushLogEntry.MarkFailed(
            tenant.Info.Id, "shopee", "SKU-3", 5,
            KeyFor(tenant.Info.Id, "SKU-3", "shopee"),
            errorCode: "shopee.push.5xx",
            latencyMs: 1200,
            observedAt: ObservedAt,
            pushedAt: PushedAt
        );

        await repo.AppendAsync(entry, CancellationToken.None);

        await using var verify = new StockSyncDbContext(tenant.Options);
        var row = await verify.PushLogEntries.AsNoTracking().SingleAsync();
        row.Status.Should().Be("Failed");
        row.ErrorCode.Should().Be("shopee.push.5xx");
        row.LatencyMs.Should().Be(1200);
    }

    [Fact]
    public async Task AppendAsync_MarkBreakerOpen_PersistsWithBreakerCode()
    {
        var tenant = await _fixture.ProvisionTenantAsync("psl-breaker");
        await using var db = new StockSyncDbContext(tenant.Options);
        var repo = new PushLogRepository(db);

        var entry = PushLogEntry.MarkBreakerOpen(
            tenant.Info.Id, "shopee", "SKU-4", 9,
            KeyFor(tenant.Info.Id, "SKU-4", "shopee"),
            observedAt: ObservedAt,
            rejectedAt: PushedAt
        );

        await repo.AppendAsync(entry, CancellationToken.None);

        await using var verify = new StockSyncDbContext(tenant.Options);
        var row = await verify.PushLogEntries.AsNoTracking().SingleAsync();
        row.Status.Should().Be("BreakerOpen");
        row.ErrorCode.Should().Be("stocksync.breaker.open");
        row.LatencyMs.Should().Be(0);
    }
}
