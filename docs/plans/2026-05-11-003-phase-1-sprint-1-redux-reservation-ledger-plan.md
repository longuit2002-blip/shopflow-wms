---
title: "feat: Phase-1 Sprint-1-redux — reservation ledger under DB-per-tenant"
type: feat
status: pending
date: 2026-05-11
origin: docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md
supersedes: docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md
depends_on: docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md
---

# feat: Phase-1 Sprint-1-redux — reservation ledger under DB-per-tenant

## Overview

Sprint-1-redux ships the real `ReservationRepository` against the redesigned tenant-DB schema from Phase-0-redux U8. The hot-path conditional-CTE INSERT runs at **READ COMMITTED** isolation (not SERIALIZABLE — the v2.0 correction per Tech Design v3.0 §4.4). The W3 scale gate is reshaped from "5,000 concurrent on one tenant" to "**5 tenants × 1,000 concurrent each, with per-tenant fairness floor ≥ 0.85**" — the noisy-neighbor test that proves DB-per-tenant earns its keep.

Sprint-1 (the original) shipped these units against the RLS-shaped foundation: U1-U2 conditional INSERT + Confirm + ReleaseExpired, U3 ReservationExpiryWorker, U4 PropertyTests wired to real impl, U5 scale gate, U6 partial sign-off. **All of that work is being thrown away** as the architectural foundation pivoted under it. Sprint-1-redux re-derives equivalent units against the new foundation.

The per-unit shape closely tracks the original Sprint-1 plan's units (similar U1-U6 progression) because the *business logic* of the reservation ledger is unchanged — what changes is the *plumbing* (no `tenant_id` on entities, ReadCommitted isolation, multiplexed expiry worker, multi-tenant scale gate).

---

## Problem Frame

Phase-0-redux ships:
- Inventory module schema with NO `tenant_id` columns (per tenant DB)
- Repository skeleton with NotImplementedException placeholders
- Per-test tenant DB provisioning fixture
- PgBouncer-fronted Postgres
- `tenant.id` resource attribute on every span

Sprint-1-redux fleshes out:
1. `ReservationRepository.TryReserveAsync` — conditional-CTE INSERT at READ COMMITTED, idempotency via UNIQUE(order_id) + 23505 catch + app-level FindByOrderId short-circuit.
2. `ReservationRepository.{FindByOrderIdAsync, ConfirmAsync, ReleaseExpiredAsync}` — the rest of the port.
3. `ReservationExpiryWorker` — multiplexed across tenants per Tech Design v3.0 §11.2.
4. PropertyTests wired to real impl via per-tenant fixture.
5. Multi-tenant noisy-neighbor scale gate (the W3 gate, redux shape).
6. Sign-off + tag `v0.3.0-sprint-1-redux`.

The **business logic** is straight from Tech Design v3.0 §4. The **infrastructure** is the new shape. The split lets Sprint-1-redux move quickly because the SQL is locked.

---

## Requirements Trace

- **R1.** `TryReserveAsync(sku, qty, orderId, ct)` (no tenant_id parameter — implicit via `IRequestContext`) implements the §4.3 conditional-CTE INSERT verbatim in a `ReadCommitted` transaction. Returns `Result<Guid>.Success(id)` on insert, `Result<Guid>.Failure("oversold", "OVERSOLD")` on zero-row. **Does NOT use SERIALIZABLE. Does NOT catch 40001** (cannot occur at this isolation for this pattern).
- **R2.** Idempotency keyed on `order_id` (UNIQUE constraint per tenant DB; no tenant_id column). Layered:
  - Application-level short-circuit via `FindByOrderIdAsync` — happy path.
  - Database-level `UNIQUE(order_id)` — concurrent race safety net.
  - On `PostgresException` SqlState `23505`, re-fetch and return existing.
- **R3.** Property suite at `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` flips green-against-real-impl with **zero test-body edits**. Only the DI binding and per-property fixture change. The 5 properties exercise the lifecycle on a fresh per-tenant DB.
- **R4.** **W3 multi-tenant scale gate** (the headline test): 5 tenants × 1,000 concurrent reservations each, against `total_qty=1000` per tenant. Each tenant produces exactly 1,000 successes + 0 OVERSOLD failures (since each tenant's stock matches the demand). Cross-tenant isolation: tenant A's reservations are 0% present in tenant B's DB. Per-tenant p99 < 200ms. **Per-tenant fairness floor ≥ 0.85** (worst-tenant p99 / best-tenant p99 ≥ 0.85).
- **R5.** `ReservationExpiryWorker` is a multiplexed `BackgroundService` that polls every `ExpiryPollIntervalSeconds` (default 30s, configurable per `InventoryOptions`). Each cycle iterates active tenants from catalog, opens a brief scope per tenant, calls `ReleaseExpiredAsync`, logs.
- **R6.** `ConfirmAsync(reservationId)` returns `Result` (non-generic). In a single ReadCommitted transaction: pre-state lookup, UPDATE stock_items.total_qty, UPDATE reservations_ledger status, INSERT outbox StockChangedEvent. Error codes: NOT_FOUND, ALREADY_CONFIRMED, INVALID_STATE, STOCK_ROW_MISSING.
- **R7.** All operations honor multi-tenancy via `IRequestContext.TenantId` → DbContext factory. No `tenant_id` parameters anywhere on `IReservationRepository` interface methods. No `tenant_id` columns in entities.
- **R8.** `ShopFlow0001-0004` analyzers (locked at Error in Phase-0-redux U10) remain clean.
- **R9.** Cross-tenant routing test (`CrossTenantRoutingTests`) covers Inventory endpoints: a request with tenant A's headers cannot create a reservation in tenant B's DB.

---

## Scope Boundaries

- **Inventory module schema is locked** by Phase-0-redux U8. No schema changes here.
- **No allocation engine changes** — Phase-2 Sprint-5 work for Channel module.
- **No saga changes** — Outbound's fulfillment saga is Phase-1 Sprint-3-redux. `ConfirmAsync` is invoked from integration tests in Sprint-1-redux; saga wiring lands later.
- **No real channel adapter calls** — Phase-2.
- **Multiplexed expiry worker leader election is single-instance**. Multi-instance leader election (advisory lock) is Phase-2 work.

### Deferred to Follow-Up

- **Sprint-2-redux (W4) — Inbound module**: separate plan.
- **Sprint-3-redux (W5) — Outbound + saga**: separate plan.
- **Reconciliation watchdog** (Tech Design §4.8) — sustained-flash-sale corner case, scale-tier 3+.
- **Multi-instance expiry worker leader election** — Phase-2.
- **Per-tenant TTL configuration** in catalog — Phase-2.

---

## Context & Research

### Relevant Code (carried from Phase-0-redux)

- `src/Services/Inventory/ShopFlow.Inventory.Domain/{StockItem, Reservation, ReservationStatus, Sku, Quantity}.cs` — entities (no TenantId field).
- `src/Services/Inventory/ShopFlow.Inventory.Domain/Events/*` — domain events with `TenantId` field set from `IRequestContext` at the outbox boundary, not on the aggregate.
- `src/Services/Inventory/ShopFlow.Inventory.Application/Ports/IReservationRepository.cs` — port (Sprint-1-redux fleshes the impl).
- `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Repositories/ReservationRepository.cs` — skeleton from Phase-0-redux U8.
- `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Migrations/20260512000001_InitialInventorySchema.cs` — schema with `[Migration]` + `[DbContext]` attributes.
- `src/Shared/ShopFlow.SharedKernel/Application/IRequestContext.cs` — tenant routing source.
- `src/Shared/ShopFlow.SharedKernel/Application/IDbContextFactory.cs` — per-request factory.
- `src/Shared/ShopFlow.SharedKernel/Infrastructure/OutboxDispatcher.cs` — multiplexed pattern; expiry worker mirrors it.
- `tests/ShopFlow.PropertyTests/Fixtures/PostgresPropertyFixture.cs` — per-tenant fixture pattern.
- `tests/ShopFlow.PropertyTests/Stubs/{NotImplementedReservationRepository,ReservationRepositoryHandle}.cs` — adapter pattern from Sprint-1 (carried forward; the static handle pattern still works).
- `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` — 5 properties, zero test-body edits per R3.

### Institutional Learnings (carry-forward)

- `docs/solutions/2026-05-10-ef-migration-needs-attributes.md` — Phase-0-redux U8 already enforces this; relevant if migrations get amended.
- `docs/solutions/2026-05-10-green-against-stub-property-suite.md` — pattern survives.
- `docs/solutions/2026-05-10-fscheck-replay-gamma-must-be-odd.md` — pinned `Replay = "(42,4243)"` seed is correct.
- `docs/solutions/2026-04-28-csproj-xml-comment-double-dash.md`, `docs/solutions/2026-04-28-central-package-management.md`, `docs/solutions/2026-04-28-test-csproj-conventions.md` — all carry forward.

### External References

- Postgres docs, "Concurrency Control" — READ COMMITTED for conditional INSERT (cited in Tech Design v3.0 §4.4 and ADR-0003).

---

## Key Technical Decisions

- **READ COMMITTED, not SERIALIZABLE.** Per Tech Design v3.0 §4.4. Conditional-INSERT correctness comes from the WHERE clause. SERIALIZABLE adds 40001 retry overhead with no correctness benefit. Repository code does not use SERIALIZABLE; does not catch 40001. If a future load test surfaces a real race, response is `SELECT ... FOR UPDATE` on the stock_items row inside the CTE, NOT bring back SERIALIZABLE.

- **`ExecuteScalarAsync` for the conditional INSERT's RETURNING.** Same pattern as Sprint-1 (archived). EF Core 8's `ExecuteSqlInterpolatedAsync` returns row count; the Guid is read via raw `NpgsqlCommand.ExecuteScalarAsync`. Parameterized via `NpgsqlParameter` collection — no SQL injection risk.

- **Idempotency layered: app-level short-circuit + DB-level UNIQUE.** Same pattern as Sprint-1 (archived). App-level handles common retry; DB-level handles race. UNIQUE(order_id) per tenant DB (not tenant_id, order_id — tenant is the database).

- **`ConfirmAsync` returns non-generic `Result`.** Sprint-1 (archived) made this correction; survives the redesign. Error codes: NOT_FOUND, ALREADY_CONFIRMED, INVALID_STATE, STOCK_ROW_MISSING.

- **`ReleaseExpiredAsync` returns released-row count and emits StockReleasedEvent per row.** Same as Sprint-1 (archived). UPDATE ... RETURNING + outbox INSERT in one transaction.

- **`ReservationExpiryWorker` is multiplexed across tenants.** New shape vs. Sprint-1 (which assumed single-tenant). Pattern: parent BackgroundService loop iterates active tenants from catalog, per-tenant scope, brief connection use, returns. Aspire AppHost in dev mode runs ONE worker process; production scales horizontally with Phase-2 leader election.

- **Per-test tenant DB for the property suite.** `PostgresPropertyFixture` from Phase-0-redux provisions a fresh tenant DB per test class, applies migrations, installs a real `ReservationRepository` into `ReservationRepositoryHandle.Current`. The 5 properties' bodies are unchanged. R3 met.

- **Multi-tenant scale gate uses 5 separate tenant DBs.** NBomber or k6 driver with 5 tenant configurations, 1,000 concurrent each. Per-tenant connection pool isolation via PgBouncer's per-database limits (default 20). Fairness floor measured as `min(p99_per_tenant) / max(p99_per_tenant)`.

---

## Open Questions

### Resolved during planning

- **Q: SERIALIZABLE vs ReadCommitted.** A: ReadCommitted, per Postgres docs. Resolved at redesign plan + Tech Design v3.0 §4.4. Sprint-1-redux executes this.
- **Q: How does the multiplexed expiry worker handle a tenant whose DB is unreachable?** A: Per-tenant try/catch around the brief connection. Failed tenant logged at Error level; next cycle retries. Other tenants unaffected.
- **Q: Does `ConfirmAsync` need to load the reservation aggregate via EF (with change tracking) or can it stay raw-SQL?** A: Raw SQL inside the transaction is the same shape as `TryReserveAsync`. The change-tracker overhead isn't justified for what is functionally a state-machine flip + decrement. Resolved.

### Deferred to implementation

- **Pool size and timeout tuning for the scale gate.** Default Npgsql pool 100 + PgBouncer per-DB limit 20 across 5 tenants = 100 connections to Postgres. Should be sufficient. If p99 misses, tune in U5 + document in `docs/solutions/`.
- **FsCheck failure message wording when invariant breaks.** Carried forward from Sprint-1; refine if confusing.
- **Log level for reservation attempts.** Information by default; downgrade if CI noisy.
- **NBomber vs k6 for the scale gate.** NBomber chosen (in-process, easier multi-tenant config, results integrate into xUnit test report). k6 deferred to Phase-2.

---

## High-Level Technical Design

> *Directional guidance — see Tech Design v3.0 §4 for the canonical SQL.*

### TryReserveAsync (interface signature change vs Sprint-1)

```csharp
// Old (Sprint-1 archived):
Task<Result<Guid>> TryReserveAsync(Guid tenantId, Sku sku, int qty, Guid orderId, CancellationToken ct);

// New (Sprint-1-redux):
Task<Result<Guid>> TryReserveAsync(Sku sku, int qty, Guid orderId, CancellationToken ct);
// Tenant context flows via IRequestContext / DbContext factory. The tenant_id parameter
// disappears because the DB itself is the tenant.
```

### Conditional INSERT at ReadCommitted (verbatim from Tech Design v3.0 §4.3)

```csharp
public async Task<Result<Guid>> TryReserveAsync(Sku sku, int qty, Guid orderId, CancellationToken ct)
{
    // App-level short-circuit
    var existing = await FindByOrderIdAsync(orderId, ct);
    if (existing is not null) return Result<Guid>.Success(existing.Id);

    var reservationId = Guid.NewGuid();
    var nowUtc = _clock.GetUtcNow().UtcDateTime;
    var expiresAt = nowUtc.AddMinutes(15);

    await using var transaction = await _db.Database
        .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);  // NOT Serializable

    var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
    Guid? insertedId;

    await using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = """
            WITH current AS (
                SELECT total_qty, allocated_qty,
                       (SELECT COALESCE(SUM(qty), 0)
                          FROM reservations_ledger
                         WHERE sku = @p_sku AND status = 'Active') AS reserved_qty
                  FROM stock_items WHERE sku = @p_sku
            )
            INSERT INTO reservations_ledger (id, sku, qty, order_id, status, reserved_at, expires_at)
            SELECT @p_id, @p_sku, @p_qty, @p_order, 'Active', @p_now, @p_expires
              FROM current
             WHERE current.total_qty - current.allocated_qty - current.reserved_qty >= @p_qty
            RETURNING id;
            """;
        cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        // parameter binding...

        try
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            insertedId = result is null or DBNull ? null : (Guid)result;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Concurrent same-order_id race; re-fetch and return existing
            await transaction.RollbackAsync(ct);
            var raceExisting = await FindByOrderIdAsync(orderId, ct);
            return raceExisting is not null
                ? Result<Guid>.Success(raceExisting.Id)
                : Result<Guid>.Failure("Idempotency conflict but no existing reservation.", "IDEMPOTENCY_CONFLICT");
        }
    }

    if (insertedId is null)
    {
        await transaction.RollbackAsync(ct);
        return Result<Guid>.Failure("oversold", "OVERSOLD");
    }

    // Emit StockReservedEvent via outbox row in same transaction
    AppendOutbox(new StockReservedEvent(_requestContext.TenantId, sku.Value, qty, insertedId.Value, orderId, nowUtc));
    await _db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);

    return Result<Guid>.Success(insertedId.Value);
}
```

### Multiplexed expiry worker

```csharp
public sealed class ReservationExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, _clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            await foreach (var tenant in _catalog.GetActiveTenantsAsync(stoppingToken))
            {
                try { await ReleaseExpiredForTenantAsync(tenant, stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "expiry worker failed for tenant {Tenant}", tenant.Slug); }
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    private async Task ReleaseExpiredForTenantAsync(TenantInfo tenant, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IRequestContext>().Bind(tenant);
        var repo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var count = await repo.ReleaseExpiredAsync(ct);
        if (count > 0) _logger.LogInformation("tenant {Tenant} released {Count} expired", tenant.Slug, count);
    }
}
```

---

## Implementation Units

### U1. `ReservationRepository.TryReserveAsync` — ReadCommitted conditional CTE

**Goal:** Implement TryReserveAsync per Tech Design v3.0 §4.3. App-level short-circuit + ReadCommitted transaction + 23505 catch + outbox StockReservedEvent. Interface signature drops the tenant_id parameter.

**Requirements:** R1, R2, R7, R8.

**Files:**
- Modify: `src/Services/Inventory/ShopFlow.Inventory.Application/Ports/IReservationRepository.cs` — drop tenant_id parameter from TryReserveAsync, FindByOrderIdAsync.
- Modify: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Repositories/ReservationRepository.cs` — flesh out NotImplementedException placeholder.
- Test: `tests/ShopFlow.Inventory.IntegrationTests/ReservationRepositoryTests.cs` — integration tests against per-test tenant DB.

**Test scenarios:**
- Happy path: TryReserveAsync against total=100 returns Success; row in ledger has status=Active, expires_at=NOW+15min.
- Boundary: qty == available succeeds; qty > available returns OVERSOLD.
- Idempotency: same orderId twice returns same Guid; ledger has 1 row.
- Concurrent same-orderId race: 2 simultaneous calls; both Success with same Guid; ledger has 1 row.
- 2 concurrent calls qty=600 each against total=1000: exactly 1 success, 1 OVERSOLD; no exceptions thrown.
- StockReservedEvent appears in outbox_messages atomic with the ledger insert.

**Verification:**
- All tests pass against Testcontainers Postgres.
- No SERIALIZABLE in code. No 40001 catch. (Verified by grep.)
- ShopFlow0001-0004 clean.

---

### U2. `FindByOrderIdAsync`, `ConfirmAsync`, `ReleaseExpiredAsync` (sync method)

**Goal:** Rest of the IReservationRepository surface. ConfirmAsync returns Result with NOT_FOUND/ALREADY_CONFIRMED/INVALID_STATE codes. ReleaseExpiredAsync returns released-row count and emits StockReleasedEvent per row.

**Requirements:** R5 (sync method only — worker is U3), R6, R7, R8.

**Dependencies:** U1.

**Files:**
- Modify: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Repositories/ReservationRepository.cs`
- Test: `tests/ShopFlow.Inventory.IntegrationTests/ReservationRepositoryTests.cs`

**Test scenarios:**
- FindByOrderIdAsync after TryReserveAsync returns the row.
- FindByOrderIdAsync for non-existent orderId returns null.
- ConfirmAsync on Active flips to Confirmed + decrements stock_items.total_qty + emits StockChangedEvent. Atomic.
- ConfirmAsync on Confirmed → Result.Failure("ALREADY_CONFIRMED").
- ConfirmAsync on Released/Expired → Result.Failure("INVALID_STATE").
- ConfirmAsync on non-existent → Result.Failure("NOT_FOUND").
- ReleaseExpiredAsync flips 3 expired Active rows to Expired; returns 3; emits 3 StockReleasedEvents in same transaction.
- ReleaseExpiredAsync with 0 expired returns 0, emits 0.

**Verification:**
- All tests pass.
- ConfirmAsync atomicity: if outbox insert fails, the stock_items decrement rolls back.

---

### U3. `ReservationExpiryWorker` multiplexed background service

**Goal:** BackgroundService that polls every InventoryOptions.ExpiryPollIntervalSeconds, iterates active tenants from catalog, runs ReleaseExpiredAsync per tenant in a fresh scope.

**Requirements:** R5.

**Dependencies:** U2.

**Files:**
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Background/ReservationExpiryWorker.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Application/InventoryOptions.cs`
- Modify: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/InventoryServiceCollectionExtensions.cs` — register Configure<InventoryOptions> + AddHostedService<ReservationExpiryWorker>.
- Test: `tests/ShopFlow.Inventory.IntegrationTests/ReservationExpiryWorkerTests.cs`

**Test scenarios:**
- Worker starts, runs N cycles (configurable via interval=100ms in test), calls ReleaseExpiredAsync for each tenant.
- Multi-tenant: 2 tenants seeded with expired reservations; one worker cycle releases both, emits events to each tenant's outbox.
- Per-tenant exception isolation: if tenant A's DB throws, tenant B still gets processed.
- Cancellation: stoppingToken signals graceful shutdown; in-flight work completes, no new tenant scopes opened.
- ExpiryPollIntervalSeconds <= 0 throws at construction (validate at startup).

**Verification:**
- All tests pass against Testcontainers Postgres + multi-tenant fixture.
- Worker runs as IHostedService and survives Aspire AppHost lifecycle.
- No DateTime.Now (analyzer enforces).

---

### U4. PropertyTests wired to per-tenant fixture + real ReservationRepository

**Goal:** The 5 FsCheck properties run against real Postgres via per-tenant fixture. Test bodies unchanged (R3). NotImplementedReservationRepository continues forwarding to ReservationRepositoryHandle.Current.

**Requirements:** R3, R7, R8.

**Dependencies:** U1, U2.

**Files:**
- Modify: `tests/ShopFlow.PropertyTests/Fixtures/PostgresPropertyFixture.cs` (carried from Sprint-1) — provision fresh tenant per fixture, install real repo into Handle.Current.
- Modify: `tests/ShopFlow.PropertyTests/Stubs/NotImplementedReservationRepository.cs` — adapter pattern (carried from Sprint-1, drop tenant_id parameter from forward calls).
- Modify: `tests/ShopFlow.PropertyTests/Stubs/ReservationRepositoryHandle.cs` (carried from Sprint-1).
- **Modify: `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` ONLY for parameter signature change** — `tenant_id` parameter dropped, but assertion bodies unchanged. R3 honored: the 5 property test method bodies' assertion logic is untouched.

**Test scenarios:**
- All 5 properties pass against real Postgres.
- Per-PR CI excludes (Category=Integration); nightly runs include.
- Property 4 (ExpiryReleasesActiveRows) and Property 5 (InvariantHoldsForAnyOperationSequence) — known spec gap from Sprint-1 (assert against state-shape that needs read-back surface). Tech Design v3.0 §4 names this; resolved here OR documented as Sprint-2-redux follow-up. Plan-time decision: properties 1-3 must pass; properties 4-5 may produce shrinkable counter-examples that feed `docs/solutions/`.

**Verification:**
- 5/5 properties green against real Postgres (or documented spec gap for 4/5).
- Property method bodies unchanged from Phase-0-redux U8 baseline (verify via `git diff`).

---

### U5. W3 multi-tenant noisy-neighbor scale gate

**Goal:** The headline scale-gate test. 5 tenants × 1,000 concurrent reservations each. Asserts: cross-tenant isolation (no data leak), per-tenant correctness (1,000 successes / 0 OVERSOLD when stock matches demand), per-tenant fairness floor ≥ 0.85, per-tenant p99 < 200ms.

**Requirements:** R4.

**Dependencies:** U1, U2.

**Files:**
- Create: `tests/ShopFlow.Inventory.IntegrationTests/MultiTenantScaleGateTests.cs`
- Create supporting: `tests/ShopFlow.Inventory.IntegrationTests/ScaleGate/{TenantHarness, FairnessCalculator}.cs`

**Approach:**
- Setup: provision 5 tenant DBs (`scale-1` through `scale-5`); seed each with `total_qty=1000` for SKU `SKU-SCALE`.
- Generate 1,000 unique orderIds per tenant (5,000 total).
- Spawn 5,000 Task.WhenAll calls — each call binds to one of 5 tenants and reserves qty=1.
- Capture per-task latency via Stopwatch.
- Assertions:
  - Per-tenant: 1,000 successes (since stock matches demand exactly), 0 OVERSOLD failures, 0 other errors.
  - Per-tenant DB query: `SELECT COUNT(*) FROM reservations_ledger WHERE status='Active'` = 1,000 in each tenant DB.
  - Cross-tenant isolation: `SELECT COUNT(*) FROM reservations_ledger` in tenant scale-1 returns 1,000 (not 5,000).
  - Fairness: compute p99 latency per tenant, assert min(p99) / max(p99) ≥ 0.85.
  - Per-tenant p99 < 200ms.
- Tag `Category=Integration`. Nightly + on-demand only.

**Test scenarios:**
- Headline test: 5×1000 = 5,000 reservations; assertions per above.
- Mixed-load variant (Phase-2 work, named here for context): 1 tenant bursting 5,000, 4 tenants normal load 50 each. Fairness floor still ≥ 0.85.
- Repeatability: re-running from clean tenants produces same numbers ±10%.
- Edge: total_qty=1 in one tenant, 1,000 callers contend; 1 success, 999 OVERSOLD. (Single-tenant correctness still works under multi-tenant gate.)

**Verification:**
- Test passes against Testcontainers Postgres + PgBouncer.
- p99 captured per tenant in test output.
- Fairness floor measured and reported.

---

### U6. Sprint-1-redux sign-off

**Goal:** Wrap Sprint-1-redux. Run all gates, write sign-off doc, tag `v0.3.0-sprint-1-redux`, update README + CLAUDE current-stage line.

**Requirements:** all R-IDs (verification only).

**Dependencies:** U1-U5.

**Files:**
- Create: `docs/phase-gates/2026-05-DD-sprint-1-redux-signoff.md`
- Modify: `README.md` current-stage line.
- Modify: `CLAUDE.md` current-stage section.
- Modify: `docs/CHANGELOG.md` — Sprint-1-redux entry.
- Tag: `v0.3.0-sprint-1-redux` annotated.

**Approach:**
- Run `dotnet build --configuration Release --warnaserror` — expect 0/0.
- Run `dotnet test --filter "Category!=Integration&Category!=Load"` — expect baseline + new tests passing.
- Run `dotnet test --filter "Category=Integration"` against Docker host — capture per-tenant p99 from scale gate, total integration suite duration.
- Author sign-off doc following the shape of `docs/phase-gates/2026-05-DD-phase-0-redux-signoff.md` (Phase-0-redux U10 deliverable).
- Document deferred items: properties 4-5 spec gap (if not closed), reconciliation watchdog, multi-instance worker leader election.

**Verification:**
- Sign-off doc has measured p99 per tenant + fairness floor + total integration suite duration.
- Tag pushed.
- README + CLAUDE current-stage lines point at sign-off doc.

---

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| ReadCommitted has a race condition Postgres docs don't cover | Low | High | Property suite + multi-tenant scale gate catch any actual race. If found, add `SELECT FOR UPDATE` on stock_items inside CTE; document as `docs/solutions/` entry. Do NOT bring back SERIALIZABLE. |
| Multi-tenant fairness floor < 0.85 under default PgBouncer config | Med | Med | Tune `max_db_connections` per tenant (raise to 30); document tuning in U6 sign-off. |
| Per-test tenant DB provisioning (Phase-0-redux U9) is too slow → integration suite > 30s | Med | Low | Time-box: if exceed, fall back to per-collection tenant DB sharing within the suite. |
| Property tests 4-5 fail under real impl due to spec gap | High | Low | Documented as planning expectation. Captured as `docs/solutions/` finding for Sprint-2-redux follow-up. Not a Sprint-1-redux blocker. |
| Multiplexed expiry worker leaks DbContext scopes under tenant churn | Low | Med | Test explicitly opens 1000 scopes in tight loop; asserts no leak via tracking. Use IServiceScopeFactory.CreateAsyncScope per cycle. |
| Idempotency UNIQUE constraint exception path rarely exercised in real load | Med | Low | U1 includes deliberate concurrent-same-orderId test that triggers it. |
| Scale gate measured p99 > 200ms on dev laptop | Med | Low | Acceptable as "best-effort gate" with documented hardware caveat. Production-grade hardware re-validates Phase-2. |

---

## Documentation / Operational Notes

- Sprint-1-redux sign-off doc follows Phase-0-redux sign-off shape.
- A new `docs/solutions/` entry expected: ReadCommitted-isolation-decision-rationale.md (capturing the SERIALIZABLE → ReadCommitted reasoning so Sprint-2+ doesn't re-derive).
- No new ADRs expected — design locked by ADR-0003 + Tech Design v3.0 §4.
- README + CLAUDE current-stage update.
- Tag `v0.3.0-sprint-1-redux`.

---

## Sources & References

- Origin plan: `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md` (R7)
- Foundation plan: `docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md` (must close before this starts)
- Tech design v3.0: `docs/redesign/02-technical-design-document.md` §4 (verbatim spec for the SQL + isolation decision)
- Product plan v3.0: `docs/redesign/01-product-development-plan.md` §9.3 (Sprint-1 scope), §5.4 (scale targets including fairness floor)
- ADR-0003 (DB-per-tenant) — multi-tenancy architecture reference
- Archive references: `docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md` (the Sprint-1 plan being superseded)
- Carried-forward learnings: all 11 docs/solutions/ entries from archived Phase-0 + Sprint-1.
