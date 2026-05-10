---
title: "feat: Phase-0-redux bootstrap — DB-per-tenant foundation"
type: feat
status: active
date: 2026-05-11
origin: docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md
supersedes: docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md
---

# feat: Phase-0-redux bootstrap — DB-per-tenant foundation

## Overview

Phase-0-redux ships the redesigned foundation: control-plane catalog database, per-tenant database provisioning, per-request tenant routing, PgBouncer-fronted Postgres cluster, and the kernel + Inventory module shaped under the new architecture from ADR-0003.

This plan **supersedes** the original Phase-0 plan (`docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md`). The original plan is preserved in git history as the foundation that Sprint-1 found load-bearing defects in (silent migration no-op, RLS-shaped tests filtered out of per-PR CI). Phase-0-redux fixes both classes of defect by design:

- Migrations carry mandatory attributes; per-PR CI exercises `MigrateAsync()` against Testcontainers Postgres.
- Cross-tenant routing correctness is a per-PR test, not nightly. The single highest-stakes correctness property of the system (no cross-tenant data leak) is tested every PR.

The implementation runs on a fresh branch `feat/phase-0-redux-db-per-tenant` cut from `main` (which today has only docs and the initial commit). The existing Phase-0 + Sprint-1 work on `feat/phase-1-sprint-1` is archived per U7 of the redesign plan.

Phase-0-redux is a **2-week sprint** (W0-W2 of the 12-week roadmap). Phase-1-redux (Sprint-1-redux reservation ledger) starts after this plan closes.

---

## Problem Frame

Phase-0 (the original) shipped:
- Modular monolith with 6 .NET projects per module quartet
- Inventory blessed reference module with reservation ledger schema
- ShopFlow Roslyn analyzers ShopFlow0001-0004 at Error severity
- Aspire AppHost dev orchestrator
- Compose production handoff manifest
- GitHub Actions CI workflow
- `shopflow-gate` CLI

Three classes of defect surfaced when Sprint-1 ran integration tests on a Docker host:

1. **Silent EF migration no-op** — hand-authored migration class lacked `[Migration]` + `[DbContext]` attributes; `MigrateAsync()` returned successfully without applying. Phase-0 sign-off claimed "all gates measured" but the migration gate was structurally absent (per-PR CI excluded `Category=Integration`, hiding it).
2. **SERIALIZABLE 40001 race** — repository code wrapped the conditional-INSERT in `Serializable` isolation per Tech Design v2.0 §7.2 verbatim; Postgres correctly raised 40001 under concurrent contention but the repository did not catch it. The W3 scale gate's premise broke.
3. **RLS shape conflicts with PDPA hard isolation requirement** — surfaced by user driver re-evaluation, captured in ADR-0003.

Phase-0-redux is the architectural reset. (1) is fixed by mandatory attributes + per-PR migration smoke test. (2) is fixed by switching to READ COMMITTED per Postgres documentation for the conditional-INSERT pattern (Tech Design v3.0 §4.4). (3) is fixed by the entire pivot to DB-per-tenant.

---

## Requirements Trace

- **R1.** Solution layout matches Tech Design v3.0 §10. Includes `src/ControlPlane/` quartet (Domain, Application, Infrastructure, Migrations) and `tools/shopflow-migrate/` CLI.
- **R2.** Kernel exposes `IRequestContext` (with `TenantId`, `TenantSlug`, `DbConnectionString`), `IDbContextFactory<TContext>`, `ITenantCatalog`, multiplexed `OutboxDispatcher`, and tenant routing middleware. The v2.0 `TenancyInterceptor` is removed.
- **R3.** Control plane database (`shopflow_control`) ships with the `tenants` schema (per Tech Design v3.0 §1.5) and is migrated separately from tenant DBs.
- **R4.** `shopflow-migrate` CLI implements `provision`, `apply --target=<version>`, `archive`, `restore`, `status` subcommands. `apply` runs parallel-by-tenant with configurable concurrency (default 4).
- **R5.** Aspire AppHost dev mode includes `pgbouncer` resource, creates the control-plane DB on startup, and provisions two dev tenants (`dev1`, `dev2`) before any service starts.
- **R6.** Inventory module is rebuilt under DB-per-tenant: no `tenant_id` columns, no RLS policies, conditional-INSERT runs at `IsolationLevel.ReadCommitted`. `UNIQUE(order_id)` is the idempotency key (not `(tenant_id, order_id)`).
- **R7.** Module shape is replicated to Inbound, Outbound, Channel, Analytics as csproj scaffolding (no business logic — that lands in Phase-1+).
- **R8.** ShopFlow Roslyn analyzers ShopFlow0001-0004 are re-derived for the new architecture. New rules per AGENTS.md §3.
- **R9.** Test infrastructure: per-test tenant DB provisioning via `PostgresFixture.CreateTenantAsync()`. `CrossTenantRoutingTests` suite mandatory. **Per-PR migration smoke test** that exercises `MigrateAsync()` against Testcontainers Postgres — guards the v2.0 silent-no-op defect from recurring.
- **R10.** Observability: `tenant.id` resource attribute on every span / log / metric, set in routing middleware.
- **R11.** GitHub Actions CI workflow runs per-PR on every push: build (warn-as-error), unit tests, **migration smoke test, cross-tenant routing test**. Nightly: integration suite, property suite, scale gates.
- **R12.** `shopflow-gate` CLI carries forward; updated to surface tenant-aware checks (provisioning latency p99, catalog reachable, PgBouncer reachable, all dev tenants migrated current).
- **R13.** Phase-0-redux sign-off doc captures measured numbers for the new architecture, replaces the v2.0 sign-off as the active baseline.

---

## Scope Boundaries

### In scope

- All R1-R13 above.
- One blessed reference module (Inventory) with reservation ledger schema (no behavior — behavior is Sprint-1-redux).
- Module shape replication for the other 4 contexts (csproj scaffolds only).
- Two dev tenants provisioned on AppHost startup (`dev1`, `dev2`).
- PgBouncer single-instance dev mode + production handoff config.
- Phase-0-redux sign-off doc + tag `v0.2.0-phase-0-redux`.

### Out of scope (deferred to Phase-1+)

- Reservation ledger **behavior** (TryReserve / Confirm / ReleaseExpired) — this is Sprint-1-redux.
- Inbound, Outbound, Channel, Analytics business logic — Phase-1 sprints 2-3 + Phase-2.
- Channel adapters (Shopee, Lazada, mock servers) — Phase-2 Sprint-4.
- PgBouncer HA pair — Phase-2 deliverable.
- Multi-instance dispatcher leader election — Phase-2.
- Tenant self-service onboarding UI — Phase-3 Sprint-7.
- Cross-region tenant residency — Phase-3+.
- BYOK encryption — Phase-3+ enterprise tier.
- SOC2 / ISO 27001 controls — out of architectural scope.

### Deferred to Implementation

- Exact PgBouncer pool sizing (`max_db_connections` per tenant). Default 20; benchmark in U10 sign-off.
- Catalog cache TTL (default 5 minutes); validate in U10 with cache-stale-data scenario.
- Migration smoke test exact assertions (just "tables exist" or also "constraints exist + indexes exist"). Default to "schema matches expected" via simple `pg_dump --schema-only` comparison; refine in U8.
- Routing middleware exact priority order tests (header > JWT > subdomain) — refined in U6 with concrete test scenarios.

---

## Context & Research

### Relevant Code Patterns (from archived Phase-0)

What survives the redesign architecturally:
- **Result<T> pattern** (`src/Shared/ShopFlow.SharedKernel/Domain/Result.cs`) — kept verbatim.
- **Sku value object** (`src/Services/Inventory/ShopFlow.Inventory.Domain/Sku.cs`) — kept verbatim.
- **AggregateRoot + BaseEntity** (`src/Shared/ShopFlow.SharedKernel/Domain/`) — kept; `TenantId` field removed from BaseEntity if present.
- **MediatR pipeline behaviors** (validation, logging, tracing) — kept; tracing behavior gains `tenant.id` span attribute.
- **xUnit + FluentAssertions test conventions** (`tests/Directory.Build.props`) — kept.
- **Central Package Management** (`Directory.Packages.props`) — kept.
- **Husky.NET pre-commit + Taskfile.yml** — kept.

What is replaced wholesale:
- **`TenancyInterceptor`** → routing middleware + `IDbContextFactory<T>`.
- **Inventory schema** (composite PKs, RLS, `tenant_id` everywhere) → tenant-DB schema (no `tenant_id`, `UNIQUE(order_id)`).
- **`PostgresFixture`** test helper → `PostgresFixture` with per-test tenant DB provisioning.
- **`OutboxInterceptor`** behavior → simplified (no cross-tenant guard); pairs with multiplexed `OutboxDispatcher`.
- **ShopFlow0001-0004 analyzers** → re-derived rules per AGENTS.md §3.
- **CI workflow** → adds migration smoke test + cross-tenant routing test to per-PR; integration suite tagged for nightly.

### External References

- Postgres docs, "Concurrency Control" — READ COMMITTED for conditional INSERT pattern (cited in Tech Design v3.0 §4.4).
- PgBouncer docs, "transaction-pooling mode" — pool config and DDL-bypass guidance.
- EF Core 8 migration attributes — `[Migration]` + `[DbContext]` (cited in `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`).
- Aspire 13.3.0 — resource registration patterns for PgBouncer-as-container.

---

## High-Level Technical Design

The mechanism diagrams live in Tech Design v3.0 §1 (multi-tenancy) and §2 (provisioning). Phase-0-redux executes them. Key load-bearing pieces:

### Routing middleware

```csharp
public sealed class TenantRoutingMiddleware
{
    public async Task InvokeAsync(HttpContext context, ITenantCatalog catalog, IRequestContext requestContext)
    {
        var slug = ExtractSlug(context);          // header > JWT > subdomain; conflicts → 403
        if (slug is null) { context.Response.StatusCode = 400; return; }

        var tenant = await catalog.LookupBySlugAsync(slug, context.RequestAborted);
        if (tenant is null) { context.Response.StatusCode = 404; return; }
        if (tenant.Status != TenantStatus.Ready) { context.Response.StatusCode = 503; return; }

        requestContext.Bind(tenant);              // sets TenantId, TenantSlug, DbConnectionString
        Activity.Current?.SetTag("tenant.id", tenant.Id.ToString());

        await _next(context);
    }
}
```

### Per-request DbContext factory

```csharp
public sealed class PerRequestDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext Create()
    {
        var connStr = _requestContext.DbConnectionString;
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connStr, npg =>
                npg.MigrationsHistoryTable("__ef_migrations_history", "public"))
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options, _requestContext)!;
    }
}
```

### Migration class shape (with mandatory attributes)

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260512000001_InitialInventorySchema")]
public partial class InitialInventorySchema : Migration
{
    protected override void Up(MigrationBuilder mb) { ... }
}
```

### `shopflow-migrate provision` workflow

```
read catalog row by slug; assert status=='pending'; lock row
update status='provisioning'; commit
connect to control-plane Postgres as superuser
CREATE DATABASE shopflow_t_<slug>
foreach module DbContext:
    factory.Create().Database.MigrateAsync()
seed module defaults
create tenant DB user with DML-only privs
update catalog status='ready', provisioned_at=NOW()
emit TenantProvisioned to control-plane outbox
```

---

## Implementation Units

### W0 — Decisions and scaffolding

#### U1. ADR review + plan-of-plans verification

**Goal:** Verify ADRs 0001/0002 postscripts + ADR-0003 are accepted and consistent. Verify the redesign plan + implementation plans are linked correctly.

**Requirements:** none directly; gates the rest.

**Files:**
- Read: `docs/adr/0001-aspire-vs-docker-compose.md`, `docs/adr/0002-modular-monolith-first.md`, `docs/adr/0003-database-per-tenant-for-compliance.md`
- Read: `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md`, this file, `docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md`
- Read: `docs/redesign/01-product-development-plan.md`, `docs/redesign/02-technical-design-document.md`
- Read: `AGENTS.md` §3 redux + audit

**Verification:**
- Every ADR / plan / canon doc links to ADR-0003 where appropriate.
- `AGENTS.md` references match the v3.0 doc structure.
- The plan-of-plans U7 archive strategy has not yet executed — branch and tag rename happens in U10 of this plan, not U7 of the redesign plan, to avoid moving git references mid-redesign.

#### U2. Repo skeleton — solution + Directory.Build.props + Directory.Packages.props + .gitattributes

**Goal:** New `feat/phase-0-redux-db-per-tenant` branch from `main`. Solution file, build props, package versions, .gitattributes carrying forward from archived Phase-0 (these are infra-level, not multi-tenancy-shaped).

**Requirements:** R1 (partial — the solution structure).

**Files:**
- Create: `feat/phase-0-redux-db-per-tenant` branch from `main`.
- Create: `ShopFlow.sln` (regenerated; same projects as v3.0 §10 layout).
- Copy from archive: `Directory.Build.props`, `Directory.Packages.props`, `.gitattributes`, `.gitignore`, `Taskfile.yml`, `.editorconfig`, Husky.NET pre-commit hook, README.md stub.
- Update README.md "Current stage" line to point at this plan.

**Approach:**
- `git checkout main && git checkout -b feat/phase-0-redux-db-per-tenant`.
- Cherry-pick infra files from `feat/phase-1-sprint-1` (the archive branch). Do not cherry-pick any business code or RLS-shaped files.
- Verify clean build with `dotnet build` against the empty solution skeleton.

**Verification:** `dotnet build` succeeds with 0/0 against the empty solution.

#### U3. Test fixtures from archive — Shopee + Lazada channel JSON

**Goal:** Bring forward synthetic-but-realistic channel webhook fixtures (`tests/fixtures/channels/{shopee,lazada}/`). Tenancy-agnostic data; survives the redesign.

**Requirements:** none directly; supports U7 mock servers later.

**Files:**
- Cherry-pick from archive: `tests/fixtures/channels/shopee/`, `tests/fixtures/channels/lazada/`, with their READMEs.

**Verification:** Files in place, README explains real-vs-synthetic disposition.

---

### W1 — Foundation: kernel, control plane, dev tenants

#### U4. ShopFlow.SharedKernel + analyzers (re-derived)

**Goal:** Ship the kernel under v3.0 shape: `IRequestContext` with routing fields, `IDbContextFactory<T>`, `ITenantCatalog`, `OutboxDispatcher` (multiplexed pattern), tenant routing middleware. Re-derive analyzers ShopFlow0001-0004.

**Requirements:** R2, R8.

**Dependencies:** U2.

**Files:**
- Create: `src/Shared/ShopFlow.SharedKernel/{Domain,Application,Infrastructure}/...`
  - `Domain/`: BaseEntity, AggregateRoot, ValueObject, Result, Result<T>, IDomainEvent (verbatim from archive)
  - `Application/`: IRequestContext (new shape), IDbContextFactory<T> interface, ITenantCatalog interface, MediatR pipeline behaviors (validation, logging, tracing — tracing emits `tenant.id`)
  - `Infrastructure/`: PerRequestDbContextFactory<T>, OutboxDispatcher (multiplexed BackgroundService), TenantRoutingMiddleware, OutboxInterceptor (simplified), PostgresOutboxMessage entity
- Create: `src/Shared/ShopFlow.SharedKernel.Analyzers/Rules/`
  - ShopFlow0001 — no raw DbSet outside repository
  - ShopFlow0002 — no IPublishEndpoint.Publish during write transaction
  - ShopFlow0003 — no DbContext instantiation outside factory; no explicit connection strings in business code
  - ShopFlow0004 — no IRequestContext re-validation in handlers; no DateTime.Now
- Create: `src/Shared/ShopFlow.Contracts/IntegrationEvents/...` — events with `tenant_id` in envelope

**Approach:**
- Domain primitives (Result, BaseEntity, AggregateRoot, ValueObject) cherry-picked from archive; remove `TenantId` from BaseEntity.
- IRequestContext is new — interface with TenantId, TenantSlug, DbConnectionString, CorrelationId, UserId, IsFeatureEnabled.
- PerRequestDbContextFactory<T> reads connection string from IRequestContext at factory.Create() time.
- OutboxDispatcher is `BackgroundService` that loops: get active tenants from catalog → for each, open scoped DbContext → claim batch from outbox_messages → publish to RabbitMQ → mark processed.
- TenantRoutingMiddleware extracts slug per priority order, looks up via ITenantCatalog (cached), populates IRequestContext.
- Analyzer rules: write each as Roslyn `DiagnosticAnalyzer`. ShopFlow0001 detects `_db.<DbSet>.Where` outside `*Repository` classes. ShopFlow0003 detects `new DbContext(...)` outside factory. ShopFlow0004 detects `_requestContext.TenantId == ...` re-validation pattern in `*Handler.cs`.

**Test scenarios (in `tests/ShopFlow.SharedKernel.UnitTests/`):**
- IRequestContext bind tests (slug → tenant lookup → fields populated)
- TenantRoutingMiddleware: header-priority test, JWT-priority test, conflict → 403, non-existent slug → 404, status≠Ready → 503
- PerRequestDbContextFactory: each Create() returns fresh DbContext with correct connection string
- OutboxDispatcher: multi-tenant fan-out test (mock catalog returns 3 tenants, each has 5 outbox rows; dispatcher publishes 15 messages in 3 transactions)
- Analyzer rules: positive cases (rule violations detected) + negative cases (clean code passes)

**Verification:**
- `dotnet build` clean.
- All unit tests pass.
- ShopFlow analyzers at Warning severity in this unit; promoted to Error in U11.

#### U5. ShopFlow.ControlPlane project quartet + catalog migration

**Goal:** Control-plane database project with the tenant catalog schema. Migrations target `shopflow_control`.

**Requirements:** R1, R3.

**Dependencies:** U4.

**Files:**
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Domain/Tenant.cs` (aggregate root, lifecycle states)
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Domain/TenantStatus.cs` (enum: Pending, Provisioning, ProvisioningFailed, Ready, Archiving, Archived)
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Application/Ports/ITenantCatalog.cs`
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Application/Ports/IChannelDirectory.cs`
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Infrastructure/ControlPlaneDbContext.cs`
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Infrastructure/Repositories/TenantCatalog.cs`
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Infrastructure/EntityConfigurations/TenantConfiguration.cs`
- Create: `src/ControlPlane/ShopFlow.ControlPlane.Migrations/20260512000000_InitialControlPlaneSchema.cs` (with mandatory `[Migration]` + `[DbContext]` attributes)

**Approach:**
- Catalog schema per Tech Design v3.0 §1.5: `tenants(id UUID PK, slug TEXT UNIQUE, db_name TEXT UNIQUE, region TEXT, tier TEXT, status TEXT, business_reg TEXT, sub_processors JSONB, created_at, provisioned_at, archiving_at, archived_at, breach_notified_at)`.
- Plus `tenant_events(id UUID, tenant_id UUID, event_type TEXT, payload JSONB, occurred_at TIMESTAMPTZ)` for audit.
- Plus `channel_connections(channel_id UUID PK, tenant_id UUID, channel_type TEXT, secret_encrypted BYTEA, created_at TIMESTAMPTZ)` — the inbound webhook routing source.
- TenantCatalog implementation has in-memory LRU cache (size 1000, TTL 5min).
- ControlPlaneDbContext does NOT use IDbContextFactory<T> — it has a fixed connection string (the control-plane DB), wired via `services.AddDbContext<ControlPlaneDbContext>(...)` in the composition root.

**Test scenarios (in `tests/ShopFlow.ControlPlane.IntegrationTests/`):**
- Provision a tenant via TenantCatalog: creates row in Pending, transition to Provisioning, then Ready
- Cache hit returns same TenantInfo; cache miss queries DB; cache TTL expiry triggers re-query
- Lookup by slug returns the right tenant; non-existent slug returns null
- Status transitions are validated: Ready → Archiving allowed, Pending → Ready forbidden
- Channel-to-tenant lookup returns correct tenant_id

**Verification:**
- Migrations apply cleanly on a fresh Postgres.
- All integration tests pass.
- ControlPlaneDbContext schema matches Tech Design v3.0 §1.5 verbatim.

#### U6. shopflow-migrate CLI tool

**Goal:** Per-tenant migration runner. Subcommands: `provision`, `apply`, `archive`, `restore`, `status`, `provision --catalog`.

**Requirements:** R4.

**Dependencies:** U5.

**Files:**
- Create: `tools/shopflow-migrate/Program.cs`
- Create: `tools/shopflow-migrate/Commands/{Provision,Apply,Archive,Restore,Status}Command.cs`
- Create: `tools/shopflow-migrate/shopflow-migrate.csproj`
- Update: `Taskfile.yml` to add `task migrate:apply`, `task migrate:provision -- --tenant=<slug>` shortcuts

**Approach:**
- Use `System.CommandLine` (or `Spectre.Console.Cli` if simpler) for argument parsing.
- `provision` connects as Postgres superuser, creates DB, runs all module migrations, seeds defaults, creates app user.
- `apply --target=<version> --concurrency=N` reads ready tenants from catalog, parallel-by-tenant migration apply with bounded concurrency via `Parallel.ForEachAsync`.
- `archive --tenant=<slug>` updates catalog status, revokes user, schedules DROP after retention window. Phase-0-redux MVP: archive does the status flip + user revoke synchronously; the deferred DROP is a Phase-2 cron job. Document this honestly.
- `restore --tenant=<slug>` reverses archive while still within window.
- `status` prints per-tenant migration state across all tenants.
- `provision --catalog` is the bootstrap: creates `shopflow_control` itself + applies catalog migrations.

**Test scenarios (in `tests/ShopFlow.Migrate.IntegrationTests/`):**
- `provision --catalog` against fresh Postgres: catalog DB created, migrations applied, schema correct.
- `provision --tenant=t1`: catalog row created, tenant DB created, all module migrations applied.
- Idempotent re-provision from `provisioning_failed` state: continues from missing step.
- `apply --target=<latest>` against 5 ready tenants in parallel: all 5 advance to current version.
- `apply` failure on one tenant stops new batches, reports failure, exits non-zero. Re-running succeeds for the failed tenant after the underlying issue is fixed.
- `archive --tenant=t1` flips status, revokes user, blocks new connections. App requests for t1 return 503 (archiving) or 404 (archived).
- `restore` reverses if within window.

**Verification:**
- All CLI subcommands work as documented.
- Provisioning latency p99 < 60s per tenant (per R5 + Tech Design v3.0 §2.2).
- All integration tests pass.

#### U7. Aspire AppHost — Postgres + PgBouncer + control plane + dev tenants

**Goal:** Aspire dev orchestrator: Postgres, PgBouncer, Redis, RabbitMQ, observability stack, mock channel servers (placeholder for Phase-2), control-plane DB created on startup, two dev tenants provisioned on startup.

**Requirements:** R5, R10.

**Dependencies:** U6.

**Files:**
- Create: `src/AppHost/ShopFlow.AppHost/Program.cs`
- Create: `src/AppHost/ShopFlow.AppHost/PgBouncerConfig.cs` (generates pgbouncer.ini from dev tenant list)
- Create: `infrastructure/pgbouncer/pgbouncer.ini.template`
- Create: `infrastructure/docker-compose.yml` (production handoff — same services, no Aspire)
- Create: `infrastructure/docker-compose.prod.yml` (production overrides)

**Approach:**
- AppHost registers: postgres, pgbouncer (depends on postgres), redis, rabbitmq, seq, tempo, otel-collector, prometheus, minio, shopee-mock (placeholder — empty Node project), lazada-mock (placeholder).
- AppHost adds a startup hook that: (1) waits for postgres ready, (2) calls `shopflow-migrate provision --catalog`, (3) calls `shopflow-migrate provision --tenant=dev1` and `--tenant=dev2`, (4) signals services to start.
- PgBouncer config generated dynamically: each dev tenant gets a pool entry pointing at its DB. Uses transaction-pooling mode, max_db_connections=20.
- Compose files describe the same services in a non-Aspire form for production handoff.

**Test scenarios:**
- `task up` cold-starts in < 90s (relaxed from < 60s; provisioning adds time, documented in U10 sign-off).
- After `task up`, request `GET http://localhost:5000/api/health` with `X-ShopFlow-Tenant: dev1` returns 200 from the dev1 DB.
- Same request with `X-ShopFlow-Tenant: dev2` returns 200 from the dev2 DB.
- Request with no tenant header returns 400.
- Request with `X-ShopFlow-Tenant: unknown` returns 404.
- PgBouncer reachable on `localhost:6432`; admin console shows connection counts per database.

**Verification:**
- All Aspire resources reach healthy state.
- Cold-start time captured for U10 sign-off.
- Dev tenants provisioned and reachable.

---

### W2 — Inventory blessed module + replicate shape + analyzers locked

#### U8. Inventory module — Domain + Application + Infrastructure (schema only, no behavior)

**Goal:** Inventory module redesigned under DB-per-tenant: no `tenant_id` columns, schema-only (entity configs + migration), reservation ledger SQL ready for Sprint-1-redux.

**Requirements:** R6.

**Dependencies:** U4, U5.

**Files:**
- Create: `src/Services/Inventory/ShopFlow.Inventory.Domain/{StockItem,Reservation,ReservationStatus,Sku,Quantity,StockAdjustmentReason}.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Domain/Events/{StockChanged,StockReserved,StockReleased,StockAdjusted}Event.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Application/Ports/{IReservationRepository,IStockItemRepository,IUnitOfWork}.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/InventoryDbContext.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/EntityConfigurations/...`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Repositories/{ReservationRepository,StockItemRepository,InventoryUnitOfWork}.cs` (skeleton — `throw new NotImplementedException()` for Sprint-1-redux to flesh out)
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/InventoryServiceCollectionExtensions.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Migrations/20260512000001_InitialInventorySchema.cs` (with mandatory attributes)
- Create: `src/Services/Inventory/ShopFlow.Inventory.Api/Program.cs` + controllers (skeleton — return 501 Not Implemented for now)

**Approach:**
- Schema per Tech Design v3.0 §4.2: stock_items (sku PK, no tenant_id, row_version with `txid_current()::text::xid` default), reservations_ledger (id PK, sku FK, UNIQUE(order_id), no tenant_id), stock_adjustments, outbox_messages (per-tenant outbox).
- Migration class carries `[Migration("20260512000001_InitialInventorySchema")]` + `[DbContext(typeof(InventoryDbContext))]` — non-negotiable per AGENTS.md rule 23.
- Repository methods are `throw new NotImplementedException("Sprint-1-redux behavior — see plan 003")` placeholders. Property suite + integration tests catch the NotImplementedException as expected-stub-state per the W1/W3 pattern from `docs/solutions/2026-05-10-green-against-stub-property-suite.md` (carried forward).
- `AddInventoryModule(IConfiguration)` registers IReservationRepository, IStockItemRepository, IUnitOfWork, the InventoryDbContext via `IDbContextFactory<InventoryDbContext>`, and the (Sprint-1-redux) ReservationExpiryWorker as a hosted service (NotImplemented placeholder body).

**Verification:**
- `dotnet build` clean.
- Migration applies cleanly via `shopflow-migrate provision --tenant=test1`; schema matches Tech Design v3.0 §4.2 verbatim.
- No `tenant_id` columns anywhere in the migration.
- Repository skeleton compiles; behavior tests are red against the NotImplementedException.

#### U9. Replicate module shape: Inbound, Outbound, Channel, Analytics, Gateway

**Goal:** Module shape replicated as csproj scaffolds. No business logic; sets the layout that Phase-1 sprints fill in.

**Requirements:** R7.

**Dependencies:** U8 (Inventory is the blessed reference).

**Files:**
- Create: `src/Services/{Inbound,Outbound,Channel,Analytics}/ShopFlow.<Name>.{Domain,Application,Infrastructure,Api}/...` — one csproj quartet per module (Analytics omits Domain).
- Create: `src/ApiGateway/ShopFlow.Gateway/...` — YARP scaffolding.
- Per AGENTS.md §11 rule 79: each module has an `AGENTS.md` ≤ 50 lines, delta-only.

**Approach:**
- Each module: empty Domain (placeholder `<ModuleName>Marker.cs` so csproj has at least one file), empty Application (placeholder marker), Infrastructure with placeholder DbContext, Api with placeholder Program.cs returning 501.
- Per-module `AGENTS.md` notes module-specific invariants but is mostly empty for now (Phase-1 sprints fill in real rules).
- Gateway scaffolding: YARP routes config that reads tenant context and routes to module APIs in-process (in W1-W5 modular monolith stance). MassTransit transport bindings registered as in-memory; W6 split flips to RabbitMQ.

**Verification:**
- `dotnet build` clean across all 5 modules + gateway.
- All module test projects exist with at least one passing smoke test.
- Per-module AGENTS.md present.

#### U10. CI workflow + analyzers locked at Error + sign-off

**Goal:** GitHub Actions workflow runs build + per-PR test suite + migration smoke test + cross-tenant routing test. Analyzers promoted from Warning → Error. Sign-off doc captures measured numbers + closes Phase-0-redux + tags `v0.2.0-phase-0-redux`.

**Requirements:** R8, R9, R11, R12, R13.

**Dependencies:** U1-U9.

**Files:**
- Create: `.github/workflows/ci.yml` (per-PR), `.github/workflows/chaos-nightly.yml` (nightly integration + scale + chaos).
- Modify: `Directory.Build.props` to promote ShopFlow0001-0004 analyzers from Warning → Error.
- Create: `tests/ShopFlow.SharedKernel.IntegrationTests/CrossTenantRoutingTests.cs` — mandatory per-PR test.
- Create: `tests/ShopFlow.SharedKernel.IntegrationTests/MigrationSmokeTests.cs` — exercises `MigrateAsync()` against Testcontainers Postgres for every module's DbContext; asserts the migration was actually applied (table count > 0). Guards the v2.0 silent-no-op defect.
- Update: `tools/shopflow-gate/` — add tenant-aware checks (catalog reachable, PgBouncer reachable, dev tenants migrated current).
- Create: `docs/phase-gates/2026-05-DD-phase-0-redux-signoff.md` — measured numbers, deferred items, link to plan U IDs.
- Tag: `v0.2.0-phase-0-redux` annotated.
- Update: `README.md` "Current stage" line. Update: `CLAUDE.md` "Current stage" section.
- Create: `docs/CHANGELOG.md` entry for Phase-0-redux close.

**Approach:**
- CI per-PR: build (warn-as-error) → unit tests (filter `Category!=Integration&Category!=Load`) → cross-tenant routing test (Testcontainers) → migration smoke test (Testcontainers) → analyzer enforcement.
- CI nightly: full integration suite + property suite + scale gates (deferred to Sprint-1-redux for actual content).
- Analyzers locked: edit Directory.Build.props or .editorconfig to set ShopFlow0001-0004 severity to Error.
- shopflow-gate: add `gate phase-0-redux` subcommand that runs the structured check list from §9.2 of `01-product-development-plan.md` v3.0.
- Sign-off doc captures: build time, per-PR test count + duration, Aspire cold-start time, provisioning latency p99, dev tenant routing test results. Documents deferred items per Scope Boundaries above.

**Verification:**
- CI passes on `feat/phase-0-redux-db-per-tenant`.
- Cross-tenant routing test fails when intentionally broken (tested manually before merge — toggle the middleware off, see test fail, toggle back on).
- Migration smoke test fails when migration class loses `[Migration]` attribute (tested manually).
- All analyzers at Error severity; build fails on any violation.
- Tag pushed; sign-off doc complete.

---

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Provisioning workflow has a corner case in production (e.g., user-creation race with migrations) | Med | High | Per-test tenant DB exercises provisioning every CI run. U10 sign-off captures provisioning latency p99 + failure mode catalog. |
| PgBouncer config wrong → connection exhaustion under load | Med | Med | Default `max_db_connections=20` is conservative. U10 benchmarks single-tenant load; Phase-2 noisy-neighbor gate stresses multi-tenant. |
| Aspire cold-start exceeds 90s due to provisioning overhead | Med | Low | Documented in ADR-0001 postscript. Acceptable trade-off for multi-tenant-every-day discipline. |
| Catalog cache staleness causes routing wrong-DB for ~5 minutes after tenant change | Low | Med | TTL is conservative; tier changes are rare. Phase-2 escalates to Redis-backed pub/sub if needed. |
| Migration smoke test catches the silent no-op but the team forgets to update it as DbContexts evolve | Med | High | Smoke test is parameterized over all module DbContexts via reflection; auto-discovers new ones. New module = new test row, no manual maintenance. |
| Analyzer rule false positive blocks valid code | Med | Low | Analyzers ship at Warning in U4, promoted to Error in U10. Two-week soak finds false positives. |
| Cross-tenant routing test passes by accident (e.g., test infrastructure routes correctly even when middleware is broken) | Low | Very High | Test is structured to fail-stop on missing assertion. PR review checklist includes "did you intentionally break routing and confirm the test fails?". |
| Phase-0-redux scope expands to include Sprint-1-redux behavior | Med | Med | Strict scope boundary: Inventory module is schema-only in Phase-0-redux. Behavior is plan 003. |
| Module shape replication (U9) is overkill for Phase-0 | Low | Low | Worth the cost: locks the 6-module shape in CI from day one, prevents scope creep into module count later. |

---

## System-Wide Impact

- **Branch + tag namespace**: new branch `feat/phase-0-redux-db-per-tenant` from `main`. Old `feat/phase-1-sprint-1` archived per redesign plan U7 (executed AFTER this plan closes — to avoid moving git references mid-work).
- **`docs/solutions/`**: new entries land as Phase-0-redux discovers issues. `2026-05-10-ef-migration-needs-attributes.md` (carried forward) is the foundation for the migration smoke test.
- **`docs/CHANGELOG.md`**: new file, Phase-0-redux is its first entry.
- **Roslyn analyzers**: rules ShopFlow0001-0004 re-derived. Number assignment preserved (same indexes, new content) so external references don't churn — content shifts are documented in U10 sign-off.

---

## Documentation / Operational Notes

- The Phase-0-redux sign-off doc follows the shape of `docs/phase-gates/2026-05-10-phase-0-signoff.md` (the v2.0 sign-off, archived). New rows: cross-tenant routing test pass/fail, migration smoke test pass/fail, provisioning latency p99, PgBouncer reachable.
- Update `README.md` current-stage table: Phase-0-redux → ✅ Complete with link to the sign-off doc.
- A new `docs/solutions/` entry might land if the per-test tenant DB provisioning pattern reveals a Postgres `CREATE DATABASE` corner case (e.g., template database conflict).

---

## Sources & References

- Redesign trigger plan: `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md` (R6)
- Product plan v3.0: `docs/redesign/01-product-development-plan.md` §9.2 (Phase-0 scope)
- Tech design v3.0: `docs/redesign/02-technical-design-document.md` (canonical architecture reference for every unit)
- ADR-0001 (Aspire dev mode), ADR-0002 (modular monolith), **ADR-0003 (DB-per-tenant)**
- AGENTS.md §3 (multi-tenancy rules) — re-derived per redesign U4
- Archive references: `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` (the v2.0 plan, supersession reference)
- Carried-forward learnings: `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`, `docs/solutions/2026-05-10-green-against-stub-property-suite.md`, `docs/solutions/2026-04-28-test-csproj-conventions.md`, `docs/solutions/2026-04-28-central-package-management.md`
- External: PgBouncer transaction-pooling docs; Aspire 13.3.0 resource registration patterns; Postgres `CREATE DATABASE` semantics.
