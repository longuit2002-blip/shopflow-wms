# ShopFlow WMS — Technical Design Document

**Architecture, scale reasoning, SLO design, and multi-tenancy mechanics**

- **Version**: 3.0 (redesign — see `docs/CHANGELOG.md` and `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md`)
- **Last Updated**: 2026-05-11
- **Companion doc**: `01-product-development-plan.md` v3.0 (product scope, compliance scope, phase roadmap)
- **Supersedes**: `02-technical-design-document.md` v2.0 (April 2026), which assumed RLS-on-shared-DB tenancy.

---

## 0. Reader's Guide

This document opens with **multi-tenancy** (§1) and **provisioning** (§2) because every other architectural decision is downstream of those choices. v2.0 placed multi-tenancy at §4 and treated it as a deferred concern; v3.0 treats it as the foundation.

Sections in order:
1. **Multi-Tenancy Model** — DB-per-tenant on shared cluster, control plane, per-request routing
2. **Provisioning Workflow** — tenant lifecycle, `shopflow-migrate`, RTBF mechanics
3. **System Overview** — bounded contexts, modular monolith stance
4. **Reservation Ledger** — the hot-path correctness story (read-committed isolation, conditional CTE)
5. **Outbox + Sync Engine** — per-tenant outbox, multiplexed dispatcher, channel rate limiting
6. **Webhook Idempotency** — `(channel_id, provider_event_id)` UNIQUE per tenant DB
7. **Observability** — `tenant.id` resource attribute on every signal
8. **Scale-Tier Roadmap** — 5 → 50 → 500 tenants
9. **Architecture Decision Log** — ADR index + summary
10. **Solution Layout** — solution structure, csproj graph
11. **Bounded Contexts** — module-by-module concrete design
12. **Pick Wave Pipeline** — bounded `Channel<T>` flow
13. **Analytics** — CQRS read model, CDC story
14. **Data Platform** — partitioning, retention, backups, PgBouncer
15. **Security** — defense-in-depth, secret management, PII
16. **Deployment** — Aspire dev mode, Compose production, migration runner
17. **Testing Strategy** — per-test tenant DB, multi-tenant integration, noisy-neighbor scale gates
18. **Shared Kernel** — kernel surface area
19. **Database Schemas** — per-bounded-context summary
20. **Advanced .NET Techniques** — picked with business justification
21. **What's Deliberately Not In This Document**

---

## 1. Multi-Tenancy Model

This is the foundation chapter. Every subsequent decision assumes the model defined here.

### 1.1 Decision

**One Postgres DATABASE per tenant**, all tenants on a shared Postgres cluster, with **PgBouncer in transaction-pooling mode** as the connection multiplexer in front of the cluster. A separate **control-plane database** (`shopflow_control`) holds the tenant catalog. Tenant identity is resolved on every HTTP request from headers / JWT claim / subdomain; per-request DI scope binds the correct connection. Cross-tenant operations are impossible by construction — there is no SQL access path that sees more than one tenant's data.

This decision is captured in **ADR-0003** (Database-per-tenant for compliance hard isolation) and supersedes the v2.0 RLS-on-shared-DB stance.

### 1.2 Why this model

The driver is **PDPA SEA compliance** (Vietnam Decree 13/2023, Singapore PDPA). Auditor questions like "how do you guarantee data segregation?" are answered concretely: two different connection strings, two different `DROP DATABASE` blast radii, demonstrable in 30 seconds at the SQL prompt. RLS is a logical guarantee defensible to engineers but harder to defend to compliance auditors who do not read PostgreSQL source code.

Secondary benefits the model unlocks:
- **Right-to-erasure becomes `DROP DATABASE`** (after retention window). Verifiable, fast, no orphan rows.
- **Per-tenant backup lineage** — point-in-time-restore one tenant without affecting others.
- **Per-tenant migration** — schema evolution can be staged across tenants for risky changes.
- **Noisy-neighbor mitigation** is straightforward: PgBouncer caps `max_db_connections` per tenant database.
- **Operationally simple per-tenant ops** — disable a tenant by dropping its DB user, period.

### 1.3 Why NOT each alternative

| Alternative | Why rejected |
|---|---|
| **RLS on shared DB** (v2.0 stance) | Logical separation only. Auditor pushback. SQL injection in one tenant's code path can reach another. App-bug `WHERE` clause omission silently leaks. Backup is one big lineage. RTBF is a `DELETE` with cascade, not a `DROP`. |
| **Schema-per-tenant** | Same compliance posture as RLS (auditor sees one DATABASE). Migration scaling is N×; N=50 means every schema change applies 50 times serially or with ad-hoc parallelism. `pg_catalog` bloat at hundreds of schemas. Connection pooling fragments. |
| **Cluster-per-tenant** (one Postgres instance per tenant) | Highest isolation but operational cost is enterprise-tier. Container overhead, monitoring overhead, backup orchestration overhead. Deferred to Phase-3+ for explicit enterprise customers. |
| **Tier-based hybrid** (RLS for free tier + DB-per-tenant for paid) | Dual mental model. Two code paths for tenant context. Two test surfaces. Compliance posture is the weakest of the two paths because auditor scope is "all customer data". Explicitly rejected at redesign time. |

### 1.4 Cluster topology

```
                ┌─────────────────────────────────────┐
                │        Application Tier             │
                │  (Inventory / Inbound / Outbound /  │
                │   Channel / Analytics / Gateway)    │
                └────────────┬────────────────────────┘
                             │
                             ▼
                  ┌──────────────────────┐
                  │  PgBouncer           │  transaction-pooling mode
                  │  (HA pair @ Phase-3) │  per-database connection caps
                  └────────────┬─────────┘
                               │
                               ▼
        ┌─────────────────────────────────────────────────┐
        │             Postgres Cluster (regional)         │
        │                                                 │
        │   ┌─────────────────┐                           │
        │   │ shopflow_control│  control plane: catalog   │
        │   └─────────────────┘                           │
        │                                                 │
        │   ┌─────────────────┐                           │
        │   │ shopflow_t_acme │  tenant: acme.shopflow.app│
        │   └─────────────────┘                           │
        │                                                 │
        │   ┌─────────────────┐                           │
        │   │ shopflow_t_beta │  tenant: beta.shopflow.app│
        │   └─────────────────┘                           │
        │                                                 │
        │   ... up to N tenants per cluster (target 50)   │
        └─────────────────────────────────────────────────┘
```

All tenants on one cluster at Phase-1. Phase-2 validates 25-50 tenants on the same cluster. Phase-3+ introduces a routing layer that maps tenants to one of multiple clusters (sharding) — out of portfolio scope, but the connection-string-from-catalog pattern is the seam where it slots in.

### 1.5 Control-plane catalog

The control-plane database (`shopflow_control`) carries tenant metadata. It is the only shared data store and contains **no end-customer business data, no PII**. Catalog access is via `ITenantCatalog` only; business code never reads from `shopflow_control` directly.

Schema sketch (refined in §11.1):

```sql
CREATE TABLE tenants (
    id              UUID PRIMARY KEY,
    slug            TEXT NOT NULL UNIQUE,           -- immutable; derives db_name
    db_name         TEXT NOT NULL UNIQUE,           -- 'shopflow_t_<slug>'
    region          TEXT NOT NULL,                  -- 'sg-central-1', 'vn-central-1'
    tier            TEXT NOT NULL DEFAULT 'standard', -- 'standard' | 'enterprise'
    status          TEXT NOT NULL,                  -- see §2.1 lifecycle
    business_reg    TEXT NOT NULL,                  -- registered company number
    sub_processors  JSONB NOT NULL DEFAULT '[]',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    provisioned_at  TIMESTAMPTZ,
    archiving_at    TIMESTAMPTZ,                    -- archival initiated
    archived_at     TIMESTAMPTZ,                    -- DROP DATABASE completed
    breach_notified_at TIMESTAMPTZ
);

CREATE INDEX ix_tenants_status ON tenants (status);
CREATE INDEX ix_tenants_region ON tenants (region);
```

Why a database (not a config file, etcd, or Consul):
- Provisioning is a state machine. `BEGIN; UPDATE status = 'provisioning'; ... COMMIT` is the cheapest correct primitive.
- Catalog migrations are the same EF Core mechanism as tenant DB migrations. Only the migration *targets* differ.
- No new infrastructure dependency vs. etcd/Consul.

### 1.6 Catalog cache

Tenant lookups happen on every request. A naive query-per-request hits `shopflow_control` thousands of times per second and serializes traffic through it.

**Phase-1 implementation**: in-memory LRU cache (default size 1000, TTL 5 minutes), backed by `ITenantCatalog`. Cache miss → query → populate → return. TTL-only invalidation accepts brief staleness on tenant tier changes (5 minutes worst case); operationally acceptable because tier changes are rare.

**Phase-2+**: if cache pressure or staleness becomes an issue, escalate to Redis-backed cache with pub/sub invalidation on tenant lifecycle events (the catalog raises events to the platform-wide outbox, dispatcher publishes to a Redis channel, every app instance invalidates).

### 1.7 Per-request routing

Routing happens in **ASP.NET Core middleware**, before any handler runs. Resolution priority:

1. **Explicit header** `X-ShopFlow-Tenant: <slug>` — used for API clients and internal admin tools.
2. **JWT claim** `tenant_slug` — used for authenticated user-bound requests; the claim is signed by the auth service and carries the tenant the user belongs to.
3. **Subdomain** `<slug>.shopflow.app` — used for hosted UI and webhook receivers.

Conflicts (e.g., header says A, JWT says B) are rejected with 403 — never silently resolved. The middleware:

1. Extracts the candidate slug.
2. Calls `ITenantCatalog.LookupBySlugAsync(slug)` (cached).
3. Verifies tenant `status == 'ready'`. Other states return 503 (`provisioning`, `archiving`) or 404 (`archived`).
4. Sets `IRequestContext.TenantId`, `TenantSlug`, `DbConnectionString`.
5. Sets the OpenTelemetry resource attribute `tenant.id` on the active activity for the rest of the request.

Code below the middleware reads `IRequestContext` and trusts it. Re-validation in handlers is forbidden by analyzer rule (see §15.x); the middleware is the single trust boundary.

### 1.8 Per-request DbContext factory

EF Core `DbContext` is scoped to the request. The kernel provides `IDbContextFactory<TContext>` that resolves the connection string from `IRequestContext.DbConnectionString` and constructs the DbContext fresh per request. **No shared DbContext across tenants. No mutating connection strings on an existing DbContext** — that path leaks across requests due to EF Core's internal model cache keyed on connection.

PgBouncer is the connection pool. Npgsql still has its own pool but configured to be small (10-20 per app instance per database) so PgBouncer carries the multiplexing load. PgBouncer's **transaction-pooling mode** means a connection is checked out for the duration of one transaction, then returned. This is the only sustainable shape with 25-50 tenant DBs and N app instances — session pooling would burn `N × tenant_count` connections at idle.

### 1.9 Background workers and tenant context

Background workers (outbox dispatcher, expiry worker, channel sync) do not have an HTTP request to extract tenant from. Two patterns:

- **Per-message workers** (MassTransit consumers): every message carries `tenant_id` in its headers. Consumer middleware reads the header, opens a scope, sets `IRequestContext.TenantId`, resolves `IDbContextFactory<TContext>` for that tenant. The pattern mirrors HTTP routing.

- **Per-tenant scheduled workers** (outbox dispatcher, expiry worker): a parent process iterates active tenants from the catalog. For each tenant, it opens a brief scope with `IRequestContext.TenantId` set, runs the per-tenant work, returns. PgBouncer makes per-tenant connections affordable (one open transaction at a time per worker per tenant, returned promptly).

This is documented in §5 (outbox dispatcher mechanics) and §11 (expiry worker mechanics).

### 1.10 Verification

Tenant routing correctness is the highest-stakes test in the suite. Verified at three points:

1. **Middleware unit test** — every conflict, every priority order, every status state.
2. **Per-PR integration test** (`CrossTenantRoutingTests`) — boots a Testcontainers Postgres, provisions two tenants `t-alpha` and `t-beta`, makes a request as tenant alpha that would return tenant beta's rows if routing were broken; expects 404 / empty.
3. **Phase-1 scale gate** (`MultiTenantNoisyNeighborTests`) — 5 concurrent tenants under load, asserts data integrity and per-tenant fairness floor.

A routing leak is a P0 production incident.

---

## 2. Provisioning Workflow

### 2.1 Tenant lifecycle states

```
       register          provision          ready          archive          retain
pending ────────► provisioning ────► ready ───────► archiving ─────► archived
   │                    │              │
   │                    ▼              ▼
   │            provisioning_failed  (can re-provision via operator command)
   │
   └──► (cancelled before provision)
```

| State | Meaning | Transitions out |
|---|---|---|
| `pending` | Catalog row created, no DB yet | → `provisioning` (operator/auto trigger) |
| `provisioning` | Workflow running: CREATE DATABASE → migrations → seed | → `ready` (success) or `provisioning_failed` |
| `provisioning_failed` | Workflow bombed mid-run | → `provisioning` (retry) or hold for manual fix |
| `ready` | DB exists, migrations current, accepting traffic | → `archiving` (RTBF or operator decision) |
| `archiving` | Marked for deletion, still queryable during retention window | → `archived` (after retention) or → `ready` (restore) |
| `archived` | DROP DATABASE complete; row retained for audit | terminal (or → `pending` for slug reuse, requires manual override) |

State transitions are written via the catalog only, never inferred. All transitions emit a control-plane event (catalog has its own outbox).

### 2.2 Provisioning workflow steps

`shopflow-migrate provision --tenant=<slug>` runs:

1. Read catalog row by slug; assert `status == 'pending'`. Lock row.
2. Update `status = 'provisioning'`. Commit.
3. Connect to control-plane Postgres as superuser. Issue `CREATE DATABASE shopflow_t_<slug>`. (PgBouncer requires admin connection for this; documented in §14.5.)
4. Connect to the new tenant DB via `IDbContextFactory<TContext>` for each module DbContext.
5. Run `Database.MigrateAsync()` on each module's DbContext. **Every migration MUST carry `[Migration("...")]` and `[DbContext(typeof(...))]` attributes** — see `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`. The migration smoke test in per-PR CI guards this contract.
6. Seed module defaults (e.g., default warehouse zones, default channel mapping rules). Seed data is module-owned and idempotent.
7. Create the tenant DB user with restricted privileges (no DDL, no superuser, only DML on tenant tables). Application connection string uses this user.
8. Update catalog `status = 'ready'`, `provisioned_at = NOW()`. Emit `TenantProvisioned` event.

If any step fails, set `status = 'provisioning_failed'`, record the error, and surface to operator. The workflow is idempotent — re-running it from `provisioning_failed` continues from the next missing step (CREATE DATABASE has `IF NOT EXISTS` semantics in modern Postgres; migrations are inherently idempotent via `__EFMigrationsHistory`).

Provisioning latency target: **p99 < 60s**. Dominated by `CREATE DATABASE` and migration apply. Validated in Phase-0 scale gate.

### 2.3 Right-to-Erasure (RTBF) workflow

`shopflow-migrate archive --tenant=<slug>` runs:

1. Assert `status == 'ready'`. Update `status = 'archiving'`, `archiving_at = NOW()`. Commit.
2. Disable the tenant's DB user (revoke connect privilege) so no new traffic can reach the DB. Active sessions are terminated.
3. Wait for retention window (default 30 days, configurable per-tenant via catalog field, minimum 7 days for accidental-archive recovery).
4. After retention: `DROP DATABASE shopflow_t_<slug>`. Update `status = 'archived'`, `archived_at = NOW()`. Emit `TenantArchived` event.

`shopflow-migrate restore --tenant=<slug>` (during `archiving` window only):
1. Assert `status == 'archiving'` and `archiving_at + retention_window > NOW()`.
2. Re-enable DB user. Update `status = 'ready'`, clear `archiving_at`.

The retention window is **explicitly part of the PDPA compliance story**: PDPA does not mandate immediate deletion, only that erasure is honored within a reasonable window. 30 days protects against operator error; less than 7 days is rejected at command time.

### 2.4 Migration runner: parallel-by-tenant

`shopflow-migrate apply --target=<version> --concurrency=4`:

1. Read all `ready` tenants from catalog.
2. For each tenant, in batches of `concurrency`:
   - Open scoped DbContext for tenant
   - Check pending migrations
   - Apply via `Database.MigrateAsync()`
   - Log success / capture error
3. On any tenant failure: stop new batch starts (in-flight batches complete), report failed tenants, exit non-zero. The next run resumes from where this one left off because `__EFMigrationsHistory` is per-tenant.

`shopflow-migrate status` displays per-tenant migration state across the fleet; useful for verifying schema drift after a partial apply.

Concurrency default 4 is conservative — Postgres `CREATE INDEX CONCURRENTLY` operations and other expensive DDL benefit from running serial-ish. The `--concurrency` flag is explicit so risky migrations can ramp down.

### 2.5 Dev mode provisioning

The Aspire AppHost provisions two dev tenants on startup:
- `dev1` (slug=`dev1`, business_reg=`DEV-001`, region=`sg-central-1`)
- `dev2` (slug=`dev2`, business_reg=`DEV-002`, region=`sg-central-1`)

This keeps multi-tenant code paths exercised every working day. A request to `localhost:5000` with header `X-ShopFlow-Tenant: dev1` reaches `shopflow_t_dev1`; same request with `dev2` reaches `shopflow_t_dev2`. Routing leaks fail in development immediately, not in a staging environment.

### 2.6 Test mode provisioning

Per-test tenant DB. `PostgresFixture.CreateTenantAsync()` provisions a fresh tenant DB before each test (or test class), runs migrations, and tears down on dispose. Test latency: ~100-200ms per `CREATE DATABASE` + migrations. Acceptable for integration tier; unit tests do not hit the DB.

This pattern catches provisioning bugs at the test level, every CI run. The original v2.0 design used a shared schema across tests with `tenant_id` filtering; v3.0 abandons this in favor of physical isolation per test.

---

## 3. System Overview

### 3.1 Bounded contexts

Six contexts, same as v2.0:

```
┌──────────── Web (Next.js 14 + React Query + SignalR client) ─────────┐
│                                  │                                   │
│                      ┌───────────▼────────────┐                       │
│                      │  Gateway (YARP)        │                       │
│                      │  + tenant-routing mw   │                       │
│                      └───────────┬────────────┘                       │
│                                  │                                   │
│  ┌──────────┬───────────┬────────┴─────────┬───────────┬──────────┐  │
│  │ Inventory│  Inbound  │   Outbound +     │ Channel + │ Analytics│  │
│  │          │           │   Saga           │ Sync      │ (read)   │  │
│  └──────────┴───────────┴──────────────────┴───────────┴──────────┘  │
│           │              in-process MediatR (W1-W5)                  │
│           │              MassTransit / RabbitMQ (W6+ split)          │
│  ┌────────▼────────────────────────────────────────────────────┐     │
│  │ PgBouncer → Postgres cluster                                 │     │
│  │   shopflow_control (catalog)                                  │     │
│  │   shopflow_t_<slug> × N (tenant DBs)                          │     │
│  │ + Redis · RabbitMQ · Outbox (per tenant DB) · OTel/Tempo · Seq│     │
│  └──────────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────┘
```

Per ADR-0002, the bootstrap stance is **modular monolith first, mechanical 6-process split is a planned W6 event**. The split is orthogonal to the multi-tenancy pivot — both can land independently. The split timing is unchanged from v2.0.

### 3.2 Data ownership

| Module | Owns | Reads cross-module via |
|---|---|---|
| Inventory | `stock_items`, `reservations_ledger`, `stock_adjustments`, `outbox_messages` (per tenant) | own DB only |
| Inbound | `purchase_orders`, `receivings`, `outbox_messages` (per tenant) | events from Inventory |
| Outbound | `orders`, `pick_waves`, `shipments`, saga state, `outbox_messages` | events from Inventory + Channel |
| Channel | `channel_connections`, `webhooks`, `sync_state`, `outbox_messages` | events from Inventory |
| Analytics | read-side projections, materialized views | CDC from outbox of all modules |
| Control | `tenants`, `tenant_events` | own DB; **only** `ITenantCatalog` accessor |

Every tenant DB carries its own `outbox_messages` table; the dispatcher (§5) is the only cross-tenant consumer.

---

## 4. Reservation Ledger

This is the hot-path correctness centerpiece. It's also the section with the largest v2.0 → v3.0 correction: **READ COMMITTED isolation, not SERIALIZABLE.**

### 4.1 The hot-key problem

A flash sale on a single SKU means 10K buyers racing for 100 units in a 60-second window. Any design that serializes all requests through a single locked row produces a queue; at 30K req/s peak, the queue is the bottleneck and p99 latency collapses.

### 4.2 The pattern: append-only ledger with conditional INSERT

The inventory module owns two tables (per tenant):

```sql
CREATE TABLE stock_items (
    sku                 VARCHAR(64) PRIMARY KEY,
    name                VARCHAR(256) NOT NULL,
    category            VARCHAR(128),
    total_qty           INTEGER NOT NULL CHECK (total_qty >= 0),
    allocated_qty       INTEGER NOT NULL DEFAULT 0 CHECK (allocated_qty >= 0),
    safety_threshold    INTEGER NOT NULL DEFAULT 0,
    row_version         xid NOT NULL DEFAULT (txid_current()::text::xid),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

CREATE TABLE reservations_ledger (
    id           UUID PRIMARY KEY,
    sku          VARCHAR(64) NOT NULL REFERENCES stock_items(sku),
    qty          INTEGER NOT NULL CHECK (qty > 0),
    order_id     UUID NOT NULL UNIQUE,                -- idempotency key, scoped per tenant DB
    status       VARCHAR(16) NOT NULL CHECK (status IN ('Active','Confirmed','Released','Expired')),
    reserved_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at   TIMESTAMPTZ NOT NULL,
    finalized_at TIMESTAMPTZ
);

CREATE INDEX idx_active_reservations
    ON reservations_ledger (sku) INCLUDE (qty)
    WHERE status = 'Active';
```

**Notice what's gone**: no `tenant_id` columns, no composite primary keys, no RLS policies. The database identity is the tenant boundary; columns and constraints reflect that.

`UNIQUE(order_id)` is the idempotency key. v2.0 had `UNIQUE(tenant_id, order_id)` because tenants shared the table.

### 4.3 The conditional INSERT (verbatim)

```sql
-- Runs at READ COMMITTED. The WHERE clause is the correctness invariant;
-- the INSERT row-locks the matched stock_items row at evaluation time.

WITH current AS (
    SELECT total_qty, allocated_qty,
           (SELECT COALESCE(SUM(qty), 0)
              FROM reservations_ledger
             WHERE sku = $1
               AND status = 'Active') AS reserved_qty
      FROM stock_items
     WHERE sku = $1
)
INSERT INTO reservations_ledger (id, sku, qty, order_id, status, reserved_at, expires_at)
SELECT $2, $1, $3, $4, 'Active', NOW(), NOW() + INTERVAL '15 minutes'
  FROM current
 WHERE current.total_qty - current.allocated_qty - current.reserved_qty >= $3
RETURNING id;
```

**Zero rows returned** = insufficient stock; application returns `Result<Guid>.Failure("oversold", "OVERSOLD")`. **One row returned** = reservation succeeded and is durable in Postgres before the application sees the response.

### 4.4 Why READ COMMITTED, not SERIALIZABLE

This is the critical v2.0 correction. The v2.0 doc said:

> Serializable isolation on this transaction only.

That was wrong. v2.0 implementation surfaced `PostgresException 40001 ("could not serialize access...")` under the multi-concurrent test, and the repository did not catch it; it propagated as an unhandled error.

**Postgres's documentation for the conditional-INSERT pattern under high concurrency recommends READ COMMITTED**, not SERIALIZABLE. The reasoning:

1. The `WHERE` clause on the `SELECT INTO` evaluates against a snapshot taken at the start of the statement.
2. The INSERT `RETURNING id` either inserts one row (if the WHERE matched) or zero rows (if it didn't).
3. Under READ COMMITTED, two concurrent transactions reading overlapping `stock_items` rows both proceed; whichever commits first wins; the second sees the first's effect on the next statement (or via the `SUM(qty)` re-evaluation in its own snapshot).
4. There is no race that produces oversell because the **INSERT itself** is the serialization point — if `available >= requested` is false at INSERT time relative to the latest committed state, zero rows insert, period.

SERIALIZABLE adds nothing here except 40001 retry overhead. It would require the repository to catch 40001 and retry, adding complexity and latency for a guarantee the conditional-INSERT pattern already provides via the WHERE-clause atomicity.

The repository code does NOT use SERIALIZABLE. It does NOT catch 40001 (it cannot occur at READ COMMITTED for this pattern). If a future load test under unforeseen contention surfaces a real race, the response is to add `SELECT ... FOR UPDATE` on the `stock_items` row inside the CTE — NOT to bring back SERIALIZABLE.

This decision is documented in `docs/solutions/2026-05-DD-isolation-decision-rationale.md` (Phase-0-redux U5 deliverable).

### 4.5 Idempotency: layered

Per the v2.0 plan §U1 Key Decision (which survives the redesign):

1. **App-level short-circuit** — `TryReserveAsync` first calls `FindByOrderIdAsync(orderId)`; if a row exists, return its id as `Success`. Common-path retries skip the SERIALIZABLE round-trip.
2. **DB-level UNIQUE constraint** — `UNIQUE(order_id)` on `reservations_ledger` per tenant DB. Concurrent same-`order_id` calls: one wins the INSERT, the other gets `PostgresException 23505 (unique_violation)`. The repository catches 23505, re-fetches, returns `Success` with the existing id.

The "exception for control flow" critique applies only to the *common* path, which the app-level short-circuit handles. The 23505 catch is the rare-race safety net.

### 4.6 Confirmation and deduction

When the fulfillment saga ships, `ConfirmAsync(reservationId)` runs in a single transaction:

```sql
BEGIN;

-- 1. Pre-state lookup (also classifies state for Result code).
SELECT status, sku, qty FROM reservations_ledger WHERE id = $1;
-- Returns NOT_FOUND, ALREADY_CONFIRMED (status='Confirmed'),
-- INVALID_STATE (Released/Expired), or proceeds.

-- 2. Decrement stock_items.total_qty.
UPDATE stock_items
   SET total_qty = GREATEST(total_qty - $qty, 0),
       updated_at = NOW()
 WHERE sku = $sku
RETURNING total_qty, allocated_qty;

-- 3. Flip ledger status.
UPDATE reservations_ledger
   SET status = 'Confirmed', finalized_at = NOW()
 WHERE id = $1 AND status = 'Active';
-- 0 rows = concurrent state change; rollback and return INVALID_STATE.

-- 4. Append StockChangedEvent to outbox_messages (this DB).
INSERT INTO outbox_messages (id, event_type, payload, trace_id, created_at)
VALUES (...);

COMMIT;
```

This is the only place `stock_items` is write-contended. The fulfillment saga's upstream pick-wave queue serializes per-SKU writes, so contention is bounded.

`ConfirmAsync` returns `Result` (non-generic — no payload, just success/failure). Error codes: `NOT_FOUND`, `ALREADY_CONFIRMED`, `INVALID_STATE`, `STOCK_ROW_MISSING`.

### 4.7 Expiry

A background worker (`ReservationExpiryWorker`) runs per-tenant per cycle:

```sql
UPDATE reservations_ledger
   SET status = 'Expired', finalized_at = NOW()
 WHERE status = 'Active' AND expires_at < NOW()
RETURNING id, sku, qty, order_id;
```

For each returned row, the worker appends a `StockReleasedEvent` to the tenant's `outbox_messages`. UPDATE + outbox writes happen in one transaction.

The worker is multiplexed across tenants — see §11 (background workers) for the per-tenant scheduling pattern.

### 4.8 When the ledger breaks

If a single SKU's active-reservation count grows into the hundreds of thousands (sustained flash sale on one product), the `SUM(qty)` aggregate costs become meaningful. **Mitigation** (deferred to scale-tier 3+): maintain a rolling counter column on `stock_items` updated via trigger, and use it as the fast path; the aggregate becomes a reconciliation check that runs off-peak.

### 4.9 Domain code

```csharp
public sealed class StockItem : AggregateRoot
{
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public int TotalQuantity { get; private set; }
    public int AllocatedQuantity { get; private set; }
    public int SafetyThreshold { get; private set; }

    // No TenantId field. The database itself is the tenant.

    public void ConfirmDeduction(int qty) { ... }
    public void AdjustStock(int delta, StockAdjustmentReason reason, Guid userId) { ... }
}

public sealed record Reservation(
    Guid Id,
    string Sku,
    int Qty,
    Guid OrderId,
    ReservationStatus Status,
    DateTime ReservedAt,
    DateTime ExpiresAt,
    DateTime? FinalizedAt
)
{
    public bool IsActive(DateTime nowUtc) =>
        Status == ReservationStatus.Active && ExpiresAt > nowUtc;
}
```

Compare to v2.0: `TenantId` removed from both. Code is materially simpler; correctness lives in the routing layer above, not in the entity.

---

## 5. Outbox + Sync Engine

### 5.1 Outbox per tenant

Every tenant DB carries its own `outbox_messages` table:

```sql
CREATE TABLE outbox_messages (
    id              UUID PRIMARY KEY,
    event_type      TEXT NOT NULL,
    payload         TEXT NOT NULL,    -- JSON
    trace_id        VARCHAR(64),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at    TIMESTAMPTZ,
    retry_count     INTEGER NOT NULL DEFAULT 0,
    last_error      TEXT
);

CREATE INDEX idx_outbox_unprocessed
    ON outbox_messages (created_at)
    WHERE processed_at IS NULL;
```

Per-tenant placement is intentional. Outbox messages contain business data (qty, sku, order_id, customer references); shared placement would create exactly the cross-tenant data co-location PDPA prohibits.

Tradeoff: dispatcher complexity grows from "one polling loop" to "one polling loop per tenant". Mitigated by the multiplexed-dispatcher pattern below.

### 5.2 Multiplexed outbox dispatcher

A single `OutboxDispatcher` background process handles all tenants:

```
loop forever:
    tenants = catalog.GetActiveAsync()  // cached
    foreach tenant in tenants:
        with scope = ServiceProvider.CreateScope():
            scope.IRequestContext.TenantId = tenant.id
            scope.IRequestContext.DbConnectionString = tenant.db_connection
            db = scope.IDbContextFactory.Create()
            batch = db.OutboxMessages
                .Where(m => m.processed_at == null)
                .OrderBy(m => m.created_at)
                .Take(50)
                .ToList()
            foreach msg in batch:
                publish(msg)             // to RabbitMQ
                db.Update(msg with processed_at = now)
            db.SaveChanges()
    sleep(2 seconds)
```

PgBouncer's transaction-pooling makes per-tenant connection use cheap; the dispatcher opens a transaction, dispatches a batch, commits, releases the connection back to PgBouncer's pool. Total open connections at idle: 0. Total open connections under load: ~min(active_tenants, pool_size).

Phase-1 single-instance dispatcher is sufficient. Phase-2 introduces leader election (Postgres advisory lock via `pg_try_advisory_lock(<dispatcher_role_id>)`) so multiple instances do not double-dispatch.

### 5.3 Stock sync engine

The Channel module's stock sync engine is per-tenant:

- **Coalescing** — per `(tenant, sku, channel)` tuple, only the latest stock value in a debounce window (default 500ms) is pushed; older pending values are dropped.
- **Per-tenant per-channel rate limiting** — token bucket per (tenant, channel) tuple, sized to the marketplace's published rate limit. A flash-sale tenant burning their Shopee budget does not touch other tenants' budgets.
- **Priority queue** — flash-sale SKUs preempt regular SKUs *within a tenant*. Cross-tenant priority is never expressed; each tenant gets fair access.
- **Circuit breaker (Polly)** — per (tenant, channel) — failure of one tenant's Shopee adapter does not trip another tenant's.
- **Allocation engine** — per tenant, configurable rules.

Implementation state is in Redis, keyed `tenant:{slug}:channel:{name}:...`. Redis is shared across tenants for performance; the keying ensures isolation. Cross-tenant Redis access is forbidden by analyzer rule (no key access without tenant prefix).

### 5.4 Delivery semantics

At-least-once. Consumers are idempotent (UNIQUE constraints on inbound idempotency keys per tenant DB). Exactly-once is not architecturally guaranteed — the outbox ensures durable publish, but a consumer crash mid-handle and a redelivery can produce two attempts.

---

## 6. Webhook Idempotency

Inbound webhooks land in the tenant's own `webhook_events` table:

```sql
CREATE TABLE webhook_events (
    id                  UUID PRIMARY KEY,
    channel_id          UUID NOT NULL,
    provider_event_id   TEXT NOT NULL,
    payload             JSONB NOT NULL,
    signature_verified  BOOLEAN NOT NULL,
    received_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at        TIMESTAMPTZ,
    UNIQUE (channel_id, provider_event_id)
);
```

**Tenant routing happens before persistence**. The webhook receiver:

1. Extracts `channel_id` from the URL or signed payload.
2. Looks up `(channel_id) → tenant_id` in `shopflow_control.channel_connections`. Returns 404 if unknown.
3. Sets `IRequestContext.TenantId`, opens scoped DbContext to that tenant's DB.
4. Verifies signature (HMAC against tenant's stored channel secret).
5. INSERT INTO `webhook_events` with `(channel_id, provider_event_id)` UNIQUE — duplicate is a 200 with no work, original work is queued via outbox event.

UNIQUE constraint is **scoped to the tenant DB**, not global. A `provider_event_id` collision between tenants is structurally impossible because tenant A's webhook never lands in tenant B's table.

---

## 7. Observability

### 7.1 Three signals, one correlation ID, one tenant ID

Every span, log, and metric carries:

- `trace_id` — W3C TraceContext, propagated across services
- `correlation_id` — application-level request ID, set by gateway
- `tenant.id` — OTel resource attribute, set by routing middleware

Cross-tenant aggregation happens at the metrics layer (Prometheus / Tempo), never via SQL. A query like "platform-wide oversell rate" sums Prometheus counter `shopflow_oversell_total` grouped by no labels; a query "per-tenant oversell rate" groups by `tenant.id`.

### 7.2 Compliance signals

Three observability-driven SLAs from the compliance metrics in product plan §5.3:

- **Routing correctness**: gateway middleware emits a metric `shopflow_tenant_routing_decisions_total{outcome=success|reject|conflict}`. Sustained nonzero `reject` or `conflict` is alerting.
- **Provisioning latency**: `shopflow_tenant_provisioning_duration_seconds` histogram. SLA p99 < 60s; alert on breach.
- **Breach notification SLA**: catalog row's `breach_notified_at` minus breach detection time. Manual today; automated via runbook step that updates the catalog row at notification.

### 7.3 RED dashboards per service

Standard Rate / Errors / Duration dashboards per service, **with `tenant.id` as a slicing label**. A reviewer can answer "which tenant is producing the errors?" in two clicks.

### 7.4 Business dashboards

Stock movement, fulfillment funnel, channel sync latency, oversell rate. All grouped by `tenant.id`. Cross-tenant rollup is a separate dashboard tab, not the default view.

---

## 8. Scale-Tier Roadmap

| Tier | Tenants | Phase | What changes vs. previous tier |
|---|---|---|---|
| **Tier 1** | 1–5 | Phase-0/1 | Single Postgres cluster, single-instance PgBouncer, single-instance app, single-instance dispatcher. Sufficient for portfolio MVP. |
| **Tier 2** | 25–50 | Phase-2 | Same cluster. PgBouncer HA pair (primary + standby). Multi-instance app behind LB. Dispatcher gains leader election (advisory lock). Validated noisy-neighbor scale gates pass. |
| **Tier 3** | 50–500 | Phase-3+ (out of portfolio scope) | Sharding: routing layer maps tenants to one of N clusters. Connection-string-from-catalog pattern is the seam. Per-region clusters appear (cross-region residency). Dispatcher scales horizontally (one per cluster). |
| **Tier 4** | 500+ | Out of portfolio scope | Per-tenant cluster for enterprise tier. Dedicated PgBouncer per cluster. Customer BYOK encryption. SOC2 Type 2 control framework. |

The progression is concrete because the routing seam (`ITenantCatalog → DbConnectionString`) is the only place tier transitions need to slot in. Application code is unchanged across tiers.

---

## 9. Architecture Decision Log (abbreviated)

| ADR | Title | Status | Impact |
|---|---|---|---|
| **ADR-0001** | Aspire dev-only + hand Compose for prod | Accepted (2026-04-27), postscripted (2026-05-11) | Aspire AppHost adds PgBouncer + control-plane DB + 2 dev tenants on startup |
| **ADR-0002** | Modular monolith first, mechanical 6-process split at W6 | Accepted (2026-04-27), postscripted (2026-05-11) | Module split timing unchanged; "RLS as cheapest scale decision" claim superseded by ADR-0003 |
| **ADR-0003** | Database-per-tenant for compliance hard isolation | Accepted (2026-05-11) | This document. Foundation for §1, §2, §4-§7. Supersedes v2.0 RLS stance. |

Future ADRs land here as the design evolves. Each ADR is a `docs/adr/<number>-<slug>.md` markdown file. Postscripts are appended sections, never rewrites — ADRs are immutable.

---

## 10. Solution Layout

```
shopflow-wms/
├── src/
│   ├── ApiGateway/ShopFlow.Gateway/                   # YARP, auth, rate limit, tenant routing mw
│   │
│   ├── ControlPlane/                                   # NEW: tenant catalog
│   │   ├── ShopFlow.ControlPlane.Domain/              # Tenant aggregate, lifecycle states
│   │   ├── ShopFlow.ControlPlane.Application/         # ITenantCatalog, provisioning workflow
│   │   ├── ShopFlow.ControlPlane.Infrastructure/      # EF Core for shopflow_control
│   │   └── ShopFlow.ControlPlane.Migrations/          # Catalog DB migrations
│   │
│   ├── Services/
│   │   ├── Inventory/{Domain, Application, Infrastructure, Api}     (per-tenant DB)
│   │   ├── Inbound/{Domain, Application, Infrastructure, Api}        (per-tenant DB)
│   │   ├── Outbound/{Domain, Application, Infrastructure, Api}       (per-tenant DB, plus Sagas/)
│   │   ├── Channel/{Domain, Application, Infrastructure, Api}        (per-tenant DB, plus Adapters/, StockSync/, WebhookIngest/)
│   │   └── Analytics/{Application, Infrastructure, Api}              (CDC from per-tenant outbox)
│   │
│   ├── Shared/
│   │   ├── ShopFlow.SharedKernel/                     # BaseEntity, ValueObject, Result, IDomainEvent
│   │   │   ├── Domain/
│   │   │   ├── Application/                           # IRequestContext, IDbContextFactory<T>, MediatR behaviors
│   │   │   └── Infrastructure/                        # OutboxDispatcher (multiplexed), PerRequestDbContextFactory
│   │   ├── ShopFlow.SharedKernel.Analyzers/          # ShopFlow0001-0004 (re-derived for DB-per-tenant)
│   │   └── ShopFlow.Contracts/                        # Integration events, with tenant_id in headers
│   │
│   └── AppHost/ShopFlow.AppHost/                      # Aspire — Postgres + PgBouncer + provision dev tenants
│
├── tests/
│   ├── ShopFlow.SharedKernel.UnitTests/
│   ├── ShopFlow.ControlPlane.IntegrationTests/        # NEW: catalog + provisioning workflow
│   ├── ShopFlow.<service>.UnitTests/
│   ├── ShopFlow.<service>.IntegrationTests/           # Per-test tenant DB
│   ├── ShopFlow.PropertyTests/                        # FsCheck — multi-tenant aware
│   ├── ShopFlow.ContractTests/                        # Pact, with tenant_id in event envelope
│   ├── ShopFlow.LoadTests/                            # k6 + NBomber, including noisy-neighbor scenarios
│   └── ShopFlow.ChaosTests/                           # Toxiproxy + PgBouncer fault injection
│
├── infrastructure/
│   ├── docker-compose.yml                             # production-equivalent: Postgres + PgBouncer + Redis + RabbitMQ + observability
│   ├── docker-compose.prod.yml
│   ├── pgbouncer/pgbouncer.ini                        # NEW: transaction pooling config
│   ├── mock-channels/{shopee-mock, lazada-mock}/      # Node.js, reproduces wire protocol
│   └── otel-collector/
│
├── tools/
│   ├── shopflow-gate/                                 # Phase-gate verification CLI (carry-over)
│   ├── shopflow-migrate/                              # NEW: per-tenant migration runner CLI
│   └── extract-docs.{sh,ps1}
│
├── docs/{adr, plans, phase-gates, redesign, solutions, source}/
└── .github/workflows/{ci, release, chaos-nightly}.yml
```

Two new top-level pieces vs. v2.0:
- **`src/ControlPlane/`** — the catalog DB project tree
- **`tools/shopflow-migrate/`** — the per-tenant migration runner

---

## 11. Bounded Contexts (concrete design notes)

Each bounded context follows the same shape. v3.0 module-specific notes:

### 11.1 Control Plane

Owns `tenants` (full schema in §1.5), plus `channel_connections (tenant_id, channel_id, channel_type, secret_encrypted, created_at)` for webhook routing. The only data store with global awareness; access strictly via `ITenantCatalog` and `IChannelDirectory`.

Catalog migrations are applied on AppHost startup (dev) or via `shopflow-migrate apply --catalog` (prod). Catalog migrations run **once**, not per-tenant.

### 11.2 Inventory

Per-tenant. Entities and conditional INSERT are §4. The expiry worker (`ReservationExpiryWorker`) runs as a `BackgroundService` with the multiplexed pattern: every poll cycle, iterates active tenants, opens a brief scope, runs `ReleaseExpiredAsync` per tenant.

### 11.3 Inbound

Per-tenant. PO + receiving. MassTransit consumer for `InboundConfirmed` updates the inventory aggregate; consumer middleware sets `IRequestContext.TenantId` from the message header before resolving services.

### 11.4 Outbound

Per-tenant. Order aggregate, fulfillment saga (MassTransit state machine). Saga state is per-tenant (saga state table in tenant DB). Saga compensation on pick failure releases the reservation in the same tenant DB.

### 11.5 Channel

Per-tenant for storage (`channel_connections` joined from catalog, `sync_state` per tenant). Adapters are tenant-unaware code; tenant context flows through DI scope. Stock sync engine state in Redis with tenant-prefixed keys.

### 11.6 Analytics

CQRS read side. CDC consumer reads from each tenant's `outbox_messages` (multiplexed dispatcher pattern). Read models in tenant DB (no shared analytics DB at Phase-1 — each tenant has their own analytics tables). Phase-3 may introduce a shared analytics warehouse with row-level scoping; deferred.

---

## 12. Pick Wave Pipeline

Bounded `Channel<T>` pipeline in the Outbound module. Pattern unchanged from v2.0 — tenant context is per-message, set on consumer scope. Per-tenant queueing prevents tenant A's pick wave generation from blocking tenant B's.

---

## 13. Analytics

### 13.1 Read model

Per-tenant materialized views in tenant DB. Refresh on outbox event consumption. Common views: stock movement by SKU/date, fulfillment funnel, channel allocation efficiency.

### 13.2 Large exports

CSV export of 100K orders for one tenant uses `IAsyncEnumerable` streaming — constant memory footprint. Tenant scope is implicit (the DbContext is the tenant's). Cross-tenant export is structurally unsupported.

### 13.3 CDC at scale

Phase-3+: introduce Debezium reading per-tenant outbox tables, fanning into a shared Kafka topic with `tenant_id` partition key. Analytics consumers in a tenant-aware pattern. Out of portfolio scope; the seam is the per-tenant outbox dispatcher in §5.

---

## 14. Data Platform

### 14.1 Partitioning

Per-tenant DBs each carry their own partitioned tables where partitioning is justified (outbox monthly, audit log monthly). Tenant size is bounded enough that aggressive partitioning is not needed at Phase-1.

### 14.2 Retention

- Outbox: 12 months minimum (PDPA audit log retention), then archive to cold storage.
- Reservations ledger: 2 years (operational + dispute window).
- Catalog `tenants`: forever (terminal `archived` rows retained).

### 14.3 Backups and DR

Per-tenant DB backups via `pg_basebackup` + WAL archiving. Restore granularity = per-tenant. Cluster-level disaster recovery: failover replica in same region; cross-region is Phase-3+.

### 14.4 Replication

Logical replication per-tenant DB to a read replica for analytics offload. Phase-2 work.

### 14.5 Connection pooling — PgBouncer non-optional

PgBouncer is **non-optional from Phase-0-redux**. Without it, `N app instances × M tenants = N×M open connections` saturates Postgres before reaching scale targets.

Configuration:
- **Pool mode**: `transaction`. A connection is held for one transaction, then released.
- **Per-database limit**: `max_db_connections = 20` default. Tenant-aware: `enterprise` tier raises to 50.
- **Per-user limit**: not used (we use per-database).
- **Server reset query**: `DISCARD ALL` on connection return (clears session state).
- **Auth**: `auth_type = scram-sha-256`; PgBouncer holds a hashed secrets file that maps app users to tenant DB users.

`CREATE DATABASE` cannot run through PgBouncer — bypassed via direct admin connection in `shopflow-migrate provision`.

In dev mode, Aspire AppHost starts a single PgBouncer container with config generated from the dev tenant list. Compose production config uses an HA PgBouncer pair (Phase-2 deliverable; Phase-1 ships single-instance with documented SPOF risk).

---

## 15. Security

### 15.1 Tenant isolation (defense in depth)

Three layers, any one of which catches a cross-tenant bug:

1. **Routing middleware** — extracts tenant identity, looks up DB, sets request scope. Conflicts rejected. Status checks gate traffic.
2. **Per-request DbContext factory** — produces a DbContext bound to the resolved tenant connection. Cannot construct a DbContext "for all tenants"; the factory has no API for it.
3. **Postgres user privileges** — application connects as the tenant's DB user, which has DML-only access to that tenant's tables. SQL injection in app code cannot escalate to other DBs because the user lacks `CONNECT` privilege on them.

Every layer is independently tested.

### 15.2 Secrets

Tenant DB passwords stored encrypted in catalog (`db_password_encrypted` field, AES-256 with KEK from key vault). Channel webhook secrets stored encrypted per channel. App-level secrets via environment / key vault, never source.

### 15.3 Webhook signatures

HMAC-SHA256 against per-channel secret. Receiver verifies signature **before** persistence. Failed signature = 401, no DB write.

### 15.4 Transport and gateway

TLS 1.3 termination at gateway. mTLS internal between gateway and services (Phase-2). Rate limit at gateway by `tenant.id` from JWT/header (after routing middleware resolves it).

### 15.5 PII

End-customer PII (name, address, phone) only in tenant DB. No PII in logs. No PII in metrics. Trace span attributes carry order_id but not customer name. PDPA right-to-erasure at end-customer level is per-tenant operational policy (handled by tenant's app config); platform-level RTBF is per-tenant DROP.

### 15.6 Auth model

JWT for user auth. Token carries `sub` (user id), `tenant_slug`, `tenant_tier`, `permissions`. Token scoped per-tenant — a user belongs to one tenant. Cross-tenant users are out of scope (operator/admin tooling uses a separate auth path against the control plane only).

### 15.7 Compliance audit log

Every administrative action (provisioning, archival, tier change, sub-processor update) appends a row to `shopflow_control.tenant_events`. Audit log is queryable; tamper-detection via append-only constraint + monthly Postgres dump to off-host storage.

---

## 16. Deployment

### 16.1 Dev mode (Aspire AppHost)

`task up` brings up:
- Postgres 16 (single node)
- PgBouncer (single instance, dev config)
- Redis
- RabbitMQ
- Seq
- Tempo + OTel collector
- Prometheus
- MinIO
- Mock Shopee + Lazada
- Control-plane DB created on startup
- Two dev tenants (`dev1`, `dev2`) provisioned on startup
- All services running with the gateway

Cold start target: < 90s. Auth happy path < 150ms p99 locally.

### 16.2 Production path

Compose-based for portfolio (`docker-compose.prod.yml`). Real production at scale would use Kubernetes — compose is one container-orchestrator change away. No AWS-specific primitives. PgBouncer HA via active-passive + LB sticky-on-failure (Phase-2).

### 16.3 Migrations

Phase-0-redux ships:
- **Catalog migrations** — applied via `shopflow-migrate apply --catalog`. Run rarely, manually triggered.
- **Tenant migrations** — applied via `shopflow-migrate apply --target=<version> --concurrency=4`. Runs against all `ready` tenants. Common operation.
- **Per-PR smoke test** — Testcontainers Postgres, provisions a tenant, applies all current migrations, asserts schema matches expected. Catches the v2.0 silent-no-op bug class.

### 16.4 Feature flags

Per-tenant flags in catalog `tenants.feature_flags JSONB`. Routing middleware injects flags into `IRequestContext`. Code reads `IRequestContext.IsFeatureEnabled("...")`.

---

## 17. Testing Strategy

### 17.1 Unit tests

Pure domain. No DB. Property-based for allocation engine and reservation invariants (FsCheck). v3.0: property suites no longer reference `tenant_id` — the property's environment is "one tenant DB".

### 17.2 Integration tests

Per-test tenant DB via `PostgresFixture.CreateTenantAsync()`. Testcontainers Postgres + PgBouncer (dev mode in container). ~100-200ms per test for provisioning; acceptable.

**`CrossTenantRoutingTests`** is a mandatory suite — for every endpoint that reads/writes business data, asserts that providing a tenant-mismatched header/JWT results in 403 or 404, never the other tenant's data.

### 17.3 Multi-tenant integration tests

Two-tenant scenarios: data in tenant A is invisible from tenant B's connection. Reservation + confirmation in tenant A does not affect tenant B's stock counters. Outbox dispatcher correctly fans to RabbitMQ with `tenant_id` headers.

### 17.4 Contract tests

Pact provider/consumer per integration event. Contract includes `tenant_id` in event envelope.

### 17.5 Load tests

k6 + NBomber. Scenarios:
- **Single-tenant scale** — 5,000 concurrent reservations on one tenant (per Sprint-1 redux).
- **Noisy-neighbor (headline)** — 5 tenants × 1,000 concurrent each, fairness floor ≥ 0.85.
- **Sustained sync engine** — 2,000 stock changes/sec across 5 tenants for 5 minutes.

### 17.6 Chaos tests

Toxiproxy-driven:
- Postgres failover (one cluster instance down)
- PgBouncer down (degraded path, app retries with backoff)
- One tenant DB unavailable (other tenants must continue)
- RabbitMQ partition
- Redis down
- One channel permanently dead

### 17.7 Security / compliance tests

- Routing leak fuzzer: random tenant headers, random JWTs, expects 100% correct routing or rejection.
- SQL injection: known patterns against every tenant-scoped endpoint, expects no escalation across tenants.
- PII leakage scanner: log/metric output sampled for known PII patterns; alert on any.

### 17.8 Demo-scale "proof" test

A stranger clones the repo, runs `task up`, provisions a new tenant via CLI, reaches a working dashboard within 5 minutes. Validated each release.

---

## 18. Shared Kernel

The kernel surface area, v3.0:

```csharp
namespace ShopFlow.SharedKernel.Domain
{
    public abstract class BaseEntity { Guid Id { get; protected set; } DateTime CreatedAt; DateTime? UpdatedAt }
    public abstract class AggregateRoot : BaseEntity { /* domain events */ }
    public abstract record ValueObject;
    public sealed class Result<T> { ... }
    public sealed class Result { ... }   // non-generic, for void-returning operations like ConfirmAsync
    public interface IDomainEvent { Guid TenantId; DateTime OccurredAt; }
}

namespace ShopFlow.SharedKernel.Application
{
    // NEW shape: routing-aware
    public interface IRequestContext
    {
        Guid TenantId { get; }                      // resolved tenant uuid
        string TenantSlug { get; }                  // immutable slug (also part of db_name)
        string DbConnectionString { get; }          // resolved per-request
        string CorrelationId { get; }
        string? UserId { get; }
        bool IsFeatureEnabled(string featureName);
    }

    public interface IDbContextFactory<TContext> where TContext : DbContext
    {
        TContext Create();    // reads IRequestContext.DbConnectionString
    }

    public interface ITenantCatalog
    {
        Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct);
        IAsyncEnumerable<TenantInfo> GetActiveTenantsAsync(CancellationToken ct);
        Task UpdateStatusAsync(Guid tenantId, TenantStatus status, CancellationToken ct);
    }

    // MediatR pipeline behaviors: validation, logging, tracing, tenant-scope assertion
}

namespace ShopFlow.SharedKernel.Infrastructure
{
    // Per-request DbContext factory implementation
    public sealed class PerRequestDbContextFactory<TContext> : IDbContextFactory<TContext> { ... }

    // Multiplexed outbox dispatcher
    public sealed class OutboxDispatcher : BackgroundService { ... }

    // Tenant routing middleware
    public sealed class TenantRoutingMiddleware { ... }

    // GONE in v3.0:
    //   TenancyInterceptor — replaced by per-request DbContextFactory
    //   tenant_id stamping — no tenant_id columns to stamp
    //   query filter setup — no row-level filter; the database is the boundary
}
```

The `TenancyInterceptor` from v2.0 is removed. Its job (stamping `tenant_id` on writes, refusing cross-tenant Modified/Deleted) becomes meaningless when each tenant has its own DB. The new corresponding component is the routing middleware + per-request DbContext factory.

ShopFlow Roslyn analyzers ShopFlow0001-0004 are re-derived in Phase-0-redux. Likely shape:
- **ShopFlow0001**: no `DbContext` instantiation outside the factory.
- **ShopFlow0002**: no `IPublishEndpoint.Publish` direct call; events go through outbox.
- **ShopFlow0003**: no direct connection-string access in business code.
- **ShopFlow0004**: no `IRequestContext` re-validation in handlers (middleware is the trust boundary).

---

## 19. Database Schemas (summary)

Per tenant DB:

- **Inventory**: `stock_items`, `reservations_ledger`, `stock_adjustments`, `outbox_messages`
- **Inbound**: `purchase_orders`, `receivings`, `receiving_lines`
- **Outbound**: `orders`, `order_lines`, `pick_waves`, `pick_assignments`, `shipments`, `saga_state`
- **Channel**: `channel_settings`, `webhook_events`, `sync_state`, `product_mappings`
- **Analytics** (Phase-3+): materialized views built from outbox

Per shopflow_control DB:

- `tenants` (catalog, §1.5)
- `tenant_events` (audit log)
- `channel_connections` (channel_id → tenant_id mapping for inbound webhook routing)

No `tenant_id` columns anywhere except `channel_connections` (which is the routing source) and `tenant_events` (which is audit data).

---

## 20. Advanced .NET Techniques

Picked with business justification, v3.0 list:

- **`PerRequestDbContextFactory<T>`** — required for per-request connection string resolution; standard EF Core scoped DbContext doesn't allow this.
- **`PgBouncer` + Npgsql multi-host** — mandatory for 25-50 tenant scale.
- **`MediatR pipeline behaviors`** — for validation, logging, tracing, tenant-scope assertion.
- **`MassTransit consumer middleware`** — for tenant context propagation from message headers.
- **`Roslyn source generators`** — analyzers ShopFlow0001-0004 + (Phase-2) generated repository code for boilerplate.
- **`Channel<T>` + `BoundedChannelOptions`** — pick wave pipeline, per-tenant.
- **`IAsyncEnumerable`** — large CSV exports, constant memory.
- **`xunit` collection fixtures + `IAsyncLifetime`** — per-test tenant DB setup/teardown.
- **`OpenTelemetry resource attributes`** — `tenant.id` set in middleware, propagates to every span/log/metric.
- **`Postgres advisory locks`** — leader election for multi-instance dispatcher (Phase-2).

---

## 21. What Is Deliberately Not In This Document

- **SOC2 Type 2 control framework** — operational, not architectural.
- **Cross-region tenant residency** — Phase-3+; pattern (region field in catalog → cluster routing) is named in §8 but not implemented.
- **Per-tenant encryption-at-rest BYOK** — Phase-3+ enterprise tier.
- **Live tenant migration between clusters** — Phase-3+.
- **Schema-per-tenant explicit fallback path** — explicitly not supported. ADR-0003 closes this.
- **RLS rehabilitation** — RLS is not part of v3.0. There is no path back to row-level tenancy without a new ADR.
- **Multi-region active-active replication** — out of portfolio scope.
- **Tenant data portability tooling** (export to portable format for B2B churn) — possible follow-up.
- **Consumer-facing tenant UI for self-service archival/restore** — possible follow-up; CLI today.

---

## 22. Reading Order for a Reviewer

1. Read `01-product-development-plan.md` §3 (Tenant Model) and §4 (Compliance Scope).
2. Read this doc §1 (Multi-Tenancy) and §2 (Provisioning).
3. Skim §3 (System Overview) for the bounded contexts.
4. Read §4 (Reservation Ledger) to see the hot-path engineering.
5. Skim §5 (Outbox + Sync Engine) and §7 (Observability) for cross-cutting patterns.
6. Read §8 (Scale-Tier Roadmap) to see what changes at the next tier.
7. Skim §17 (Testing Strategy) to see what's verified.
8. Use §10 (Solution Layout) and §11 (Bounded Contexts) as a navigational reference when reading code.

The companion `01-product-development-plan.md` carries the product story. ADR-0003 explains the multi-tenancy decision. `docs/plans/2026-05-DD-002-phase-0-redux-bootstrap-plan.md` (when written) walks the implementation unit list under this design.
