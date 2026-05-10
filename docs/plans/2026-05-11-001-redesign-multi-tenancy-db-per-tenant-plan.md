---
title: "redesign: multi-tenancy pivot — RLS-shared → DB-per-tenant"
type: redesign
status: active
date: 2026-05-11
origin: docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md
supersedes: docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md
---

# redesign: multi-tenancy pivot — RLS-shared → DB-per-tenant

## Overview

This is a **plan-of-plans**: it does not implement code. It re-grounds the source-of-truth canon (`01-product-development-plan.md.docx`, `02-technical-design-document.md.docx`, ADRs, `AGENTS.md`) so that the implementation plans that follow it are built on a coherent foundation.

The trigger: while running Phase-1 Sprint-1 integration tests for the first time on a Docker-capable host, three findings landed in the same hour — (1) the EF migration class was missing `[Migration]` + `[DbContext]` attributes and silently no-op'd for the entire Phase-0; (2) under SERIALIZABLE isolation the conditional-CTE INSERT throws Postgres 40001 race errors that the repository does not catch, breaking the W3 scale gate's premise; (3) the user revisited the multi-tenancy decision and chose **DB-per-tenant on a shared Postgres cluster, driven by PDPA-region compliance**, replacing the original RLS-from-day-1 stance.

Finding (3) makes findings (1) and (2) moot in the original shape — every entity, migration, query filter, and analyzer rule was shaped by the RLS assumption. Rather than patch on top, this plan **resets Phase-0 wholesale** under the new tenancy model. Phase-0 (tag `v0.1.0-phase-0`) and the Sprint-1 work-in-progress are preserved as historical record but are not the basis for further work.

The decisions that anchor this redesign were made interactively before this plan was written, captured here as authoritative inputs:

- **Tenancy model**: 1 logical Postgres DATABASE per tenant on a shared cluster (not schema-per-tenant, not cluster-per-tenant, not tier-based hybrid).
- **Driver**: compliance / regulatory hard isolation, anchored to **PDPA Vietnam + Singapore PDPA** (SEA-focused).
- **Routing**: per-request — tenant resolved from HTTP context (header / JWT claim / subdomain), DbContext factory selects the connection per request scope; background workers carry tenant context through message headers.
- **Scale anchor**: **SaaS-credible — 25-50 validated production-ready tenants** with provisioning automation, parallel migration runner, multiplexed outbox dispatcher, and noisy-neighbor load testing.

This plan stays in the canonical `docs/plans/` directory and supersedes the Sprint-1 plan. It produces redesigned canon documents and the implementation plans that follow them; the implementation plans themselves are children of this one.

---

## Problem Frame

The two source-of-truth `.docx` documents (`01-product-development-plan.md.docx`, `02-technical-design-document.md.docx`) were written before compliance entered the user's working model. They both implicitly assume single-tenant MVP with RLS as "the cheapest scale decision in the whole design" (Tech Design §4.5). Phase-0 implementation followed those documents faithfully, building:

- Composite primary keys `(tenant_id, sku)` / `(tenant_id, id)` on every table.
- Postgres RLS policies + `SET LOCAL app.tenant_id = '...'` per request.
- Kernel `TenancyInterceptor` that stamps `tenant_id` on writes and refuses cross-tenant Modified/Deleted entries.
- EF global query filters keyed on `IRequestContext.TenantId`.
- Roslyn analyzers ShopFlow0001-0004 (multiple of which check tenant guards specific to RLS).
- Test fixtures that seed multi-tenant scenarios via raw SQL inserts with `tenant_id` columns.

Switching to DB-per-tenant invalidates all of the above without exception — `tenant_id` columns are redundant when the database itself is the boundary, RLS policies are nonsensical when each tenant has its own database, and the interceptor's job evaporates. Patching is more expensive and less honest than restarting; a clean reset gives reviewers and future-self one consistent mental model in the codebase.

The redesign is also an opportunity to fix **two latent defects** Sprint-1 surfaced:

- **Hand-authored EF migrations need `[Migration]` + `[DbContext]` attributes.** Without them `MigrateAsync()` is a silent no-op. Captured in `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`. The Phase-0-redux migration template will use `dotnet ef migrations add` exclusively, and a smoke-test category will exercise `MigrateAsync()` against Testcontainers in per-PR CI.
- **SERIALIZABLE isolation requires retry semantics.** `Repository.TryReserveAsync` runs in `IsolationLevel.Serializable` per Tech Design §7.2 verbatim, but the conditional-CTE INSERT throws `PostgresException 40001 ("could not serialize access...")` whenever two transactions touch overlapping rows. The current code does not catch 40001 and ends up reporting the error as a test failure rather than as the natural OVERSOLD or retry path. Re-design must decide: bounded retry loop (typical) or step down to `ReadCommitted` and rely on the conditional INSERT's WHERE clause for serialization (the simpler path Postgres docs actually recommend for this pattern). This is a Tech Design correction, not just an implementation tweak.

---

## Requirements Trace

This plan succeeds when the following artifacts exist, are coherent with each other, and can be handed to a fresh implementer to start Phase-0-redux without ambiguity.

- **R1. Re-grounded `01-product-development-plan.md.docx`** with explicit compliance scope (PDPA SEA), updated tenant definition (legal entity, not individual seller), updated scale roadmap anchored at 25-50 validated tenants, sub-processor disclosure section, breach notification SLA section, and right-to-erasure operational notes.
- **R2. Re-grounded `02-technical-design-document.md.docx`** with multi-tenancy as §1 (foundation for all else), provisioning workflow as §2, control-plane catalog database design, per-request routing mechanism, connection-string resolver pattern, parallel migration runner, multiplexed outbox dispatcher, PgBouncer integration notes, and noisy-neighbor mitigation strategy.
- **R3. ADR-0003** ("Database-per-tenant for hard isolation under PDPA") with status Accepted, citing the prior alternatives (RLS, schema-per-tenant, cluster-per-tenant) and the rejection rationale for each.
- **R4. ADR-0001 + ADR-0002 reconciliation** — explicit postscript on each noting which assumptions remain valid after the pivot and which have been superseded by ADR-0003. Original ADRs stay immutable; postscripts reference them.
- **R5. Re-derived `AGENTS.md` §3** (multi-tenancy rules) — replace the 4 RLS-specific instructions with the new connection-routing + tenant catalog + outbox-per-tenant rules. Other AGENTS.md sections (analyzers list, error handling, async, outbox/messaging) reviewed for consistency.
- **R6. Phase-0-redux implementation plan** (`docs/plans/2026-05-DD-002-phase-0-redux-bootstrap-plan.md`) — concrete 12-week W0-W6 unit list under the new architecture. Replaces the existing Phase-0 plan.
- **R7. Phase-1 Sprint-1-redux implementation plan** (`docs/plans/2026-05-DD-003-phase-1-sprint-1-redux-reservation-ledger-plan.md`) — re-derives the reservation-ledger work under DB-per-tenant. Replaces the existing Sprint-1 plan.
- **R8. Archive strategy** — explicit decision documented and executed for the existing `feat/phase-1-sprint-1` branch and `v0.1.0-phase-0` tag. Git history must remain navigable; future readers must be able to tell which work is current and which is historical.
- **R9. Migration-attribute regression guard** — a Phase-0-redux unit explicitly captures a Testcontainers-backed smoke test in per-PR CI that fails if any migration class is missing `[Migration]` + `[DbContext]` attributes, so the silent no-op cannot recur.
- **R10. SERIALIZABLE-vs-ReadCommitted decision** — Tech Design §4 (reservation ledger) revised to pick one isolation level and document the retry / detection contract that goes with it. The repository must catch and translate `PostgresException 40001` whenever SERIALIZABLE is retained.

---

## Scope Boundaries

This plan **only** produces planning + design artifacts. No code edits. No test edits. No CI changes. No tag manipulation. The implementation plans that follow R6 / R7 carry that work.

### In scope

- Re-writing the two `.docx` source documents (or producing markdown drafts that can replace them; the .docx format is preserved as the authoritative form per `tools/extract-docs.{sh,ps1}`).
- Writing ADR-0003 and the postscripts on ADR-0001 / ADR-0002.
- Re-deriving `AGENTS.md` §3 (and reviewing every other AGENTS.md section for downstream impact).
- Writing R6 + R7 implementation plans.
- Writing the archive strategy (R8) — but executing it (renaming branches, marking tags) is left to a single explicit follow-up commit at the end of this plan, not a separate unit.

### Out of scope

- **Implementing the redesign.** Any code edit belongs in R6 / R7 plans.
- **Running tests** against the redesigned architecture. There is no architecture to run tests against until R6 ships.
- **Schema-per-tenant or cluster-per-tenant explorations.** Decided at brainstorm time as out of scope.
- **Tier-based hybrid** (RLS for free tier + DB-per-tenant for paid tier). Decided out of scope.
- **Tenant data residency cross-region** (e.g., EU customer's tenant pinned to Frankfurt). PDPA SEA does not require it; deferred to Phase-3+.
- **Tenant-level encryption-at-rest keys (BYOK)**. Deferred to Phase-2+ enterprise-tier plan.
- **Auto-scaling tenant DB count via pgcat / Citus / Vitess**. Deferred to Phase-2+ scale-tier plan.
- **Live tenant migration between clusters.** Deferred to Phase-3+.
- **SOC2 Type 2 / ISO 27001 controls.** Compliance anchor is PDPA only at this stage; SOC2 is a follow-up product roadmap item, not architectural.
- **Per-tenant pricing / billing model.** Out of architectural scope; the catalog records `tier` for routing decisions but the billing system is downstream.

### Deferred to follow-up plans

- **R6 Phase-0-redux**: provisioning workflow CLI, control-plane catalog migrations, per-request DI scope mechanics, PgBouncer integration, parallel migration runner, multiplexed outbox dispatcher, observability tagging — each as its own unit inside R6.
- **R7 Phase-1 Sprint-1-redux**: reservation ledger conditional INSERT under the new isolation choice (per R10 decision), expiry worker scoped per-tenant, scale gate test re-derived to exercise multi-tenant noisy-neighbor (not just single-tenant 5,000 concurrent).

---

## Key Technical Decisions

These are the architectural decisions the redesign must commit to. The redesigned `02-technical-design-document.md.docx` will carry them forward verbatim; this section captures them so the document writer (Unit U2) does not need to re-derive them.

- **DB-per-tenant on a shared Postgres cluster.** Each tenant maps to one logical DATABASE. Connection lineage, backup lineage, RLS-irrelevance, and `DROP DATABASE` for right-to-erasure are the four primary wins. Trade-off: ~500-1000 DBs/instance practical ceiling before `pg_catalog` overhead bites; mitigated by sharding across multiple cluster instances at scale-tier 3+ (out of scope here).

- **Control-plane catalog database** — a single shared database (`shopflow_control`) holds the tenant directory: `tenants(id, slug, db_name, region, tier, status, created_at, ...)`. This is an *intentional* exception to "no shared database" because the catalog contains no end-customer business data — only tenant metadata. PDPA does not classify tenant company names as personal data. The catalog is the only place where a process can iterate "all tenants"; it is the routing source-of-truth.

- **Per-request routing.** ASP.NET Core middleware extracts tenant from request (priority order: explicit header → JWT claim → subdomain). Middleware looks up `db_name` in the catalog (cached, see below), sets the tenant on `IRequestContext`, and DI's scoped `InventoryDbContext` factory builds the correct connection string. No application code below the middleware is tenant-aware in routing logic.

- **Catalog cache** — tenant lookups are read-heavy. In-memory LRU (size 1000, TTL 5 minutes) cache backed by control-plane DB; cache invalidation on tenant lifecycle events via outbox. Initial implementation can be simple `IMemoryCache` with TTL; scale-tier 2+ moves to Redis-backed with pub/sub invalidation.

- **Per-request DbContext, NOT per-request connection.** Connection pooling stays via Npgsql + PgBouncer. PgBouncer in **transaction-pooling mode** is the only sustainable shape with 25-50 tenant DBs (one Npgsql pool per database burns connections fast). PgBouncer enters Phase-0-redux as a non-optional infrastructure component; Aspire AppHost includes a `pgbouncer` resource in dev mode.

- **Per-tenant outbox table** — every tenant DB carries its own `outbox_messages` table. Messages contain business data (qty, sku, order_id) which under PDPA is processor data; per-tenant storage is the consistent compliance position. Trade-off: outbox dispatcher complexity grows from "1 polling loop" to "N polling loops, one per active tenant". Mitigation: a single multiplexed dispatcher process iterates active tenants from the catalog, opens a short-lived connection to each tenant DB, claims a batch of unprocessed messages, dispatches to RabbitMQ, returns. PgBouncer makes this affordable.

- **Migration runner: parallel-by-tenant.** A new tool `shopflow-migrate` (alongside existing `shopflow-gate`) takes a target migration version and applies it to every tenant DB in parallel (configurable concurrency, default 4). On any failure, it stops, reports the failed tenant, and leaves the rest as a checkpoint for retry. Migration history table per tenant (standard EF behavior). Dev-mode AppHost runs migration on app start for the dev/test tenants only.

- **SERIALIZABLE → ReadCommitted for the conditional INSERT.** The conditional-CTE INSERT's correctness comes from its `WHERE current.total_qty - current.allocated_qty - current.reserved_qty >= @qty` clause, which is evaluated atomically on the inserting transaction's snapshot. Postgres documents this exact pattern (idempotent conditional INSERT) as safe under `READ COMMITTED` because the INSERT row-locks the target row at evaluation time. SERIALIZABLE adds nothing here except 40001 retry overhead. The repository drops to `ReadCommitted` and the analyzer rule "writes use SERIALIZABLE" (if any) is removed. This is the single most important Tech Design correction.

- **Idempotency key still on `(tenant_id, order_id)` UNIQUE** — but now `tenant_id` is implied by the database identity, not stored as a column. The constraint becomes `UNIQUE(order_id)` per tenant DB. App-level `FindByOrderIdAsync` short-circuit + DB UNIQUE 23505 catch path are unchanged in mechanics.

- **Test infrastructure: per-test tenant DB.** The Testcontainers fixture provisions a single Postgres container per test collection but creates a fresh tenant DB per integration test (or per fixture) using the same provisioning workflow production uses. This catches provisioning bugs and gives every test full isolation. Trade-off: ~50-200ms/test for `CREATE DATABASE` — acceptable for integration tier.

- **Noisy-neighbor mitigation: PgBouncer per-database connection limits.** Each tenant DB gets a configured `max_db_connections` limit in PgBouncer (default 10). A flash-sale tenant cannot starve other tenants' connection budget. Tier-aware: enterprise tier raises the limit per-tenant via catalog metadata.

---

## Open Questions

### Resolved during this plan's drafting

- **Q: Does the control-plane catalog count as a violation of the "no shared DB" stance?** A: No. The catalog stores tenant metadata (company name, slug, db_name, tier, status), not end-customer business data. PDPA does not classify tenant company names as personal data of the *end customers* whose data ShopFlow processes. The catalog is operational infrastructure, not a processor data store. Documented in Tech Design §1 explicitly.
- **Q: Can the catalog DB be replaced by config files / etcd / Consul?** A: Considered, rejected. Provisioning needs transactional state (a tenant is provisioning → migrating → ready), and a Postgres row update under `BEGIN/COMMIT` is the cheapest way to express that. Config files lose transactional guarantees; etcd/Consul add infrastructure for marginal value. Document the rejection in ADR-0003.
- **Q: Does outbox-per-tenant prevent cross-tenant aggregation analytics?** A: No, because cross-tenant analytics are explicitly out of scope under hard isolation. Aggregate metrics (e.g., "platform-wide oversell rate") are derived from observability metrics tagged with tenant_id at emission time, not from outbox data. Documented in Tech Design §7 (observability).
- **Q: SERIALIZABLE vs ReadCommitted for the conditional INSERT.** A: ReadCommitted, per Postgres documentation for the conditional INSERT pattern. Resolved here, written into Tech Design §4. Repository code must NOT use SERIALIZABLE.

### Deferred to implementation (R6 / R7)

- **Exact PgBouncer pool sizing under 25-50 tenants.** Phase-0-redux unit benchmarks default settings then tunes; documents in `docs/solutions/`.
- **Catalog cache invalidation mechanism.** First implementation: TTL-only (5 min, accepting brief staleness on tenant tier changes). Phase-2 escalates to Redis pub/sub if needed.
- **`shopflow-migrate` CLI exact UX.** Subcommands like `migrate apply --target=<version> --concurrency=4`, `migrate status`, `migrate rollback` — designed in R6.
- **Catalog schema exact columns.** First pass: `id` UUID, `slug` text UNIQUE, `db_name` text UNIQUE, `region` text, `tier` text, `status` text, `created_at` timestamptz, `provisioned_at` timestamptz nullable, `archived_at` timestamptz nullable. Refined in R6.
- **Subdomain routing** vs **path routing** vs **header routing** priority. Rough decision: header (`X-ShopFlow-Tenant`) for API, subdomain (`{slug}.shopflow.app`) for hosted UI, JWT claim for authenticated user-bound requests. Refined in R6 with concrete middleware.

---

## Implementation Units

These units produce the redesigned canon. Each is a writing task, not a coding task. Sequential dependencies because each unit's output is input to the next.

### U1. Re-write `01-product-development-plan.md.docx`

**Goal**: produce a re-grounded product development plan that takes compliance-driven hard isolation as a baseline assumption, defines tenant as a legal entity, anchors scale at 25-50 validated tenants, and explicitly enumerates PDPA SEA obligations.

**Requirements**: R1.

**Approach**:
- Preserve the existing document's structure (target customer → positioning → scope → roadmap → SLOs → risks) but rewrite each section with the new assumptions.
- New section: **Compliance scope**. Explicit list of PDPA Vietnam + Singapore PDPA obligations the system must satisfy by Phase-1 completion: data residency in-region, breach notification 72h, consent management surface, right-to-erasure operational mechanism, sub-processor disclosure list, audit log retention.
- New section: **Tenant definition**. Tenant = legal entity (registered company) operating one or more brand identities on supported marketplaces. Single-seller-individual is out of scope; the smallest tenant is a sole proprietorship with at least one verified business registration.
- Updated **Scale tiers** table:
  - Tier 1 (Phase-0-redux through Phase-1): 1-5 tenants, single Postgres cluster, validated locally
  - Tier 2 (Phase-2): 25-50 tenants, single Postgres cluster + PgBouncer, validated under noisy-neighbor load tests
  - Tier 3 (Phase-3+): 100+ tenants, sharded across multiple Postgres clusters via routing layer (out of portfolio scope)
- Updated **Risk register**: PDPA non-compliance moved from "considered" to top-3 risk with explicit mitigations. RLS removed entirely. Add: tenant DB count exhaustion, control-plane catalog availability as single-point-of-failure, PgBouncer operational risks.
- Updated **Phase roadmap**: 12 weeks unchanged, but content of each week shifts. W0 anchors compliance + control-plane catalog design; W1 ships kernel + control plane + first tenant DB; W2 lands first business module (Inventory) under DB-per-tenant; W3 = Sprint-1-redux reservation ledger; W4-W6 follow per existing shape but tenancy-aware.

**Deliverable**: `01-product-development-plan.md.docx` updated in place. The .docx remains source of truth; the redesign produces a new revision (version note inside the document records the pivot date and supersession).

**Verification**:
- A reader unfamiliar with the original can answer "What compliance regime does ShopFlow target?" "What's the smallest tenant unit?" "What's the scale ceiling for Phase-2?" from the document alone.
- No mention of RLS, `tenant_id` column, or shared-DB tenancy anywhere in the document.
- Compliance section names PDPA Vietnam + Singapore PDPA explicitly; SOC2 / ISO 27001 are explicitly out-of-scope here.

---

### U2. Re-write `02-technical-design-document.md.docx`

**Goal**: produce the technical architecture document with multi-tenancy as the foundation chapter (§1), provisioning workflow as §2, then bounded contexts and existing material re-written under the new tenancy model. Carry every Key Technical Decision from this plan into the document verbatim.

**Requirements**: R2, R10.

**Dependencies**: U1 (for product context citations).

**Approach**:
- New §1 **Multi-tenancy model**. DB-per-tenant on shared Postgres cluster. Control-plane catalog database. Per-request routing. Catalog cache. PgBouncer transaction pooling. Why-not-RLS, why-not-schema-per-tenant, why-not-cluster-per-tenant, with reference to ADR-0003.
- New §2 **Provisioning workflow**. Tenant lifecycle states (`pending → provisioning → migrating → ready → archiving → archived`). `shopflow-migrate` CLI tool. Migration runner concurrency. Provisioning steps: catalog INSERT → CREATE DATABASE → connect → apply migrations → seed tenant defaults → status update. Right-to-erasure: status `archived` → DROP DATABASE on retention window expiry. PDPA breach notification: catalog row with `breach_notified_at`; 72h SLA wired into observability alerts.
- §3 **Bounded contexts** — same 6 modules (Inventory, Inbound, Outbound, Channel, Analytics, Gateway), same modular monolith stance. References ADR-0002.
- §4 **Reservation ledger** — re-write from current §7. Conditional-CTE INSERT preserved verbatim, but isolation level is **READ COMMITTED**, retry semantics removed (the WHERE clause does the work). Idempotency UNIQUE on `(order_id)` (not `(tenant_id, order_id)` — tenant is the database). 40001 handling section deleted; replaced with brief note "ReadCommitted + WHERE-clause atomicity, no retry needed". Outbox-row append on success: same pattern, target table is the tenant's own `outbox_messages`.
- §5 **Outbox + sync engine** — re-write from current §11. Per-tenant outbox table. Multiplexed dispatcher: one process iterates active tenants from catalog, claims batch from each tenant DB's outbox, dispatches to RabbitMQ. Per-tenant rate-limit budget for channel sync (token bucket keyed on tenant_id at the dispatcher, not at the channel adapter — keeps channel adapters tenant-unaware).
- §6 **Webhook idempotency** — UNIQUE constraint on `(channel_id, provider_event_id)` per tenant DB. Same mechanism, different storage location.
- §7 **Observability** — every metric, log, and trace tagged with `tenant_id` at emission. OpenTelemetry resource attribute `tenant.id` set per request. Cross-tenant aggregation only at the metrics layer (Prometheus / Tempo), never via SQL.
- §8 **Scale-tier roadmap** — re-write from current §15. Tier 1 = 1-5 tenants single cluster; Tier 2 = 25-50 tenants single cluster + PgBouncer; Tier 3 = 100+ via sharding (deferred). Names the W6 "mechanical 6-service split" event under the new tenancy model — process split orthogonal to tenancy.
- ADR log table updated: ADR-0003 added.

**Deliverable**: `02-technical-design-document.md.docx` updated in place.

**Verification**:
- The document's §1 exists and is the multi-tenancy chapter.
- Every code excerpt, schema snippet, and diagram is consistent with DB-per-tenant — no stray `tenant_id` columns in entity diagrams.
- Reservation-ledger isolation is documented as ReadCommitted with the rationale (Postgres docs reference cited).
- Outbox dispatcher is documented as multiplexed.
- The PgBouncer requirement is named, not optional.

---

### U3. ADR-0003 + ADR-0001 / ADR-0002 postscripts

**Goal**: capture the multi-tenancy pivot as a permanent architectural decision record, and reconcile prior ADRs without rewriting them.

**Requirements**: R3, R4.

**Dependencies**: U1, U2 (for context references).

**Approach**:
- New file `docs/adr/0003-database-per-tenant-for-compliance.md`. Structure: Context (compliance driver, prior RLS stance), Decision (DB-per-tenant on shared cluster), Alternatives considered (RLS — rejected: logical-only separation; schema-per-tenant — rejected: same audit equivalence as RLS; cluster-per-tenant — rejected: ops cost out of portfolio scope; tier-based hybrid — rejected: dual mental model), Consequences (positive: clean PDPA story, simple DROP-for-erasure; negative: provisioning automation required, PgBouncer non-optional, ~500-1000 tenant ceiling per cluster), Status: Accepted (date 2026-05-11), References (this plan, U1 + U2 docs).
- Append a postscript section to `docs/adr/0001-aspire-vs-docker-compose.md` titled "Postscript 2026-05-11 — multi-tenancy pivot impact". Note: ADR-0001 stands. Aspire dev-mode adds a `pgbouncer` resource and provisions 2 dev tenants on startup.
- Append a postscript section to `docs/adr/0002-modular-monolith-first.md` titled "Postscript 2026-05-11 — multi-tenancy pivot impact". Note: ADR-0002's "modular monolith first, W6 mechanical split" stance stands. Update one bullet: the "RLS-from-day-1 = cheapest scale decision" claim is superseded by ADR-0003. Module split timing unchanged.

**Deliverable**: 1 new ADR file, 2 postscript edits.

**Verification**:
- ADR-0003 exists with status Accepted.
- ADR-0001 and ADR-0002 each end with a clearly-marked postscript section pointing at ADR-0003 and explaining what changed and what stayed.
- No ADR is rewritten in place — postscripts only.

---

### U4. Re-derive `AGENTS.md` §3 (multi-tenancy rules) and audit other sections

**Goal**: replace the RLS-specific instructions in §3 with DB-per-tenant rules; sweep the rest of `AGENTS.md` for downstream consistency.

**Requirements**: R5.

**Dependencies**: U2 (concrete architecture is input to rules).

**Approach**:
- §3 (multi-tenancy) — remove all 4 RLS rules. Add new rules:
  - "Every business write goes through `IRequestContext.TenantId` → `DbContextFactory` → tenant-specific connection string. Never construct a `DbContext` against an explicit connection string in business code."
  - "Tenant resolution happens in middleware. Application/Domain/Infrastructure code below middleware reads `IRequestContext.TenantId` and trusts it; revalidation is the middleware's job, not the handler's."
  - "Background workers carry `tenant_id` in message headers. Consumers open a scope, set `IRequestContext.TenantId` from the header, then resolve services. Never share a `DbContext` across messages."
  - "The control-plane catalog database is accessed only via `ITenantCatalog`. No business code reads from `shopflow_control` directly. Migrations to the catalog are owned by the `ShopFlow.ControlPlane` project; module migrations target tenant DBs only."
  - "`DROP DATABASE` for tenant erasure runs only via `shopflow-migrate archive --tenant=<slug>`, never from application code, never from EF migrations."
- §6 (outbox) — review for "outbox table" references; update to "tenant's own outbox table". Add: dispatcher is multiplexed, runs in a dedicated background process at scale-tier 2+.
- §11 (module shape) — review for any tenant-table references; update.
- Analyzer list (§ wherever) — note that ShopFlow0001-0004 will be re-derived in R6 / Phase-0-redux. Old rule numbers may persist with new content; the rule names matter, not the indexes.

**Deliverable**: `AGENTS.md` updated in place. Rule count change documented in commit message.

**Verification**:
- §3 contains zero references to "RLS", "row-level security", "`SET LOCAL app.tenant_id`", or "global query filter".
- §3 references the catalog database, the routing middleware, and the message-header tenant carry pattern.
- A reader can answer "How does a request reach the right database?" from §3 alone.

---

### U5. Phase-0-redux implementation plan

**Goal**: write the implementation plan that ships the redesigned foundation. Replaces `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` as the active Phase-0 plan.

**Requirements**: R6, R9 (the migration-attribute regression guard is a unit inside this plan).

**Dependencies**: U1, U2, U3, U4 (all canon must be in place before the implementation plan is written against it).

**Approach**:
- New file `docs/plans/2026-05-DD-002-phase-0-redux-bootstrap-plan.md`. Front-matter: `supersedes: docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md`.
- 12-week W0-W6 unit list, derived from U1's updated phase roadmap. Approximately 14-16 units (vs. Phase-0's 12) because the redesign adds: control-plane catalog, provisioning workflow, `shopflow-migrate` tool, PgBouncer wiring, multi-tenant test fixture, migration-attribute smoke test.
- Specific units that diverge from existing Phase-0:
  - **U1-redux** — kernel + `IRequestContext` carrying tenant routing semantics + `ITenantCatalog` + `IDbContextFactory<TContext>` per-tenant.
  - **U2-redux** — control-plane database project (`src/ControlPlane/ShopFlow.ControlPlane.{Domain,Infrastructure,Migrations}`) with the catalog schema.
  - **U3-redux** — `shopflow-migrate` CLI (apply, status, archive subcommands) with parallel-by-tenant runner.
  - **U4-redux** — Aspire AppHost: Postgres resource + PgBouncer resource + control-plane DB created on startup + 2 dev tenants provisioned on startup.
  - **U5-redux** — Inventory module redesigned: no `tenant_id` columns, no RLS, ReadCommitted for conditional INSERT (per Tech Design §4 and R10), outbox per tenant.
  - **U6-redux** — module shape replicated to Inbound/Outbound/Channel/Analytics under new shape.
  - **U7-redux** — analyzers ShopFlow0001-0004 re-derived for the new shape (no-DbContext-bypass-routing, no-cross-tenant-leak rules).
  - **U8-redux** — test infrastructure: per-test tenant DB provisioning fixture, multi-tenant integration tests, **migration-attribute smoke test that runs in per-PR CI** (R9 explicit).
  - **U9-redux** — observability: tenant_id resource attribute on every span / metric / log.
  - **U10-redux** — CI workflow updated: per-PR runs migration smoke + per-tenant integration; nightly runs noisy-neighbor + scale gate.
  - **U11-redux** — tools: `shopflow-gate` updated to surface tenant-aware checks; `shopflow-migrate` ships.
  - **U12-redux** — sign-off doc capturing measured metrics for the new architecture.
- Each unit follows the canonical template (Goal / Requirements / Dependencies / Files / Approach / Patterns to follow / Test scenarios / Verification / Execution note).

**Deliverable**: 1 new plan file, ~14-16 units.

**Verification**:
- A new implementer can pick up U1-redux of this plan and start Phase-0-redux work without reading any other planning artifact.
- Every unit references R-IDs from U1 (product) and section numbers from U2 (tech design).
- The plan supersedes the original Phase-0 plan via front-matter.

---

### U6. Phase-1 Sprint-1-redux implementation plan

**Goal**: re-derive the reservation-ledger Sprint-1 plan under DB-per-tenant + ReadCommitted. Replaces `docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md`.

**Requirements**: R7, R10.

**Dependencies**: U2 (Tech Design §4 isolation decision is the input), U5 (Phase-0-redux finishes the foundation Sprint-1-redux builds on).

**Approach**:
- New file `docs/plans/2026-05-DD-003-phase-1-sprint-1-redux-reservation-ledger-plan.md`. Front-matter: `supersedes: docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md`.
- Mostly preserves the original Sprint-1 plan's structure and unit list (U1-U6) but with these explicit corrections:
  - **Conditional INSERT runs at `ReadCommitted`**, not `Serializable`. The repository code does NOT catch `PostgresException 40001` because it does not occur at this isolation level.
  - **Idempotency UNIQUE constraint** is `UNIQUE(order_id)` per tenant DB (not `UNIQUE(tenant_id, order_id)`).
  - **App-level `FindByOrderIdAsync` short-circuit** unchanged — the pattern survives the redesign.
  - **Scale gate test** re-derived: not just 5,000 concurrent within one tenant, but **mixed-tenant noisy-neighbor**: 5 tenants × 1,000 concurrent each, all against PgBouncer-fronted shared cluster, asserting per-tenant fairness (no one tenant's success rate falls below floor) plus original p99 < 200ms target.
  - **Property suite** carries the same red-bar shape but the fixture provisions a fresh tenant DB per property class (or group of related properties) instead of seeding via raw SQL on a shared schema.
  - **Expiry worker** scoped per-tenant: the multiplexed dispatcher pattern from outbox extends to expiry. One worker process iterates active tenants, opens a brief connection to each, runs `ReleaseExpiredAsync`, moves on. Per-tenant TTL budget configurable in catalog.
  - **`StockReleasedEvent` / `StockReservedEvent` / `StockChangedEvent` outbox writes** — target tenant's own outbox table.
  - **Sign-off gates** — re-derived to include: noisy-neighbor scale gate green, multi-tenant integration suite green, control-plane catalog state consistent with provisioned tenants.

**Deliverable**: 1 new plan file, ~6 units.

**Verification**:
- The plan's R-IDs map cleanly to the redesigned Tech Design §4.
- The W3 scale gate test is multi-tenant, not single-tenant.
- The repository code description names ReadCommitted explicitly and does NOT mention SERIALIZABLE retry.

---

### U7. Archive strategy + execution

**Goal**: decide and execute the disposition of existing Phase-0 + Sprint-1 work in git so future readers can navigate history without confusion.

**Requirements**: R8.

**Dependencies**: U1-U6 complete (the new canon must exist before the old work is archived).

**Approach**:
- **Decision**: rename existing `feat/phase-1-sprint-1` branch to `archive/phase-1-sprint-1-rls-shared` and push as a permanent reference. The tag `v0.1.0-phase-0` stays as `archive/v0.1.0-phase-0-rls-shared` (annotated note explaining supersession). Both remain in the remote forever as historical record.
- A new branch `feat/phase-0-redux-db-per-tenant` is cut from `main` (which today is just the initial commit + canon docs). U5's Phase-0-redux plan executes against this new branch.
- Update `README.md` "Current stage" line: point at the redesign plan + name the archived references.
- Update `CLAUDE.md` "Current stage" section: same.
- Add a `docs/CHANGELOG.md` entry (new file) noting the pivot date, the trigger, and pointing at this plan.

**Deliverable**:
- Renamed branch (executed via `git branch -m`, push new + delete old remote)
- Annotated tag (executed via `git tag -a archive/v0.1.0-phase-0-rls-shared -m "..."`)
- README.md, CLAUDE.md, docs/CHANGELOG.md edits
- One commit on `main` recording the archival note

**Verification**:
- `git branch -a` shows the renamed branch on remote and no orphan.
- `git tag -l` shows both `v0.1.0-phase-0` and `archive/v0.1.0-phase-0-rls-shared` (or just the archive tag if the original was deleted — TBD with user).
- README.md "Current stage" points at the new plan, not the old one.
- `docs/CHANGELOG.md` carries the supersession note.

---

## System-Wide Impact

This plan does not change running code or running tests. The impact is on the **canon**:

- **Two `.docx` documents** rewritten — these are the project's source of truth. Every future plan, every future implementation, every future review measures against the new versions.
- **`AGENTS.md` §3** rewritten — auto-loaded by Claude Code / Cursor / Codex / Aider into every future session. The rule change propagates to every AI-assisted commit going forward.
- **One new ADR**, two postscripts — the architectural decision log gains a third permanent entry. The original two ADRs remain immutable but now carry "what changed since" annotations.
- **Three new implementation plans** (`docs/plans/2026-05-11-001-...` this file, `2026-05-DD-002-...` Phase-0-redux, `2026-05-DD-003-...` Sprint-1-redux). The two existing plans remain in `docs/plans/` for historical record but are explicitly marked as superseded via front-matter.
- **Branch + tag namespace** in git — `archive/` prefix makes it visually obvious which references are historical.

After this plan executes, the next session can pick up `docs/plans/2026-05-DD-002-phase-0-redux-bootstrap-plan.md` and start coding without looking at any of the prior canon. That's the success criterion.

---

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| The .docx editing path is awkward (extract → edit markdown → re-save .docx) | High | Low | The redesign produces markdown drafts in `docs/redesign/` first; user converts to .docx at U1/U2 close. Same workflow as Phase-0 used to extract for grep. |
| New canon contradicts existing canon in subtle places (e.g., AGENTS.md §11 module shape references RLS-era column names) | Med | Med | U4's audit step is explicit. Any reference to RLS-era artifacts surfaces as a `// TODO redesign` comment in the markdown draft and gets resolved before .docx save. |
| User decides mid-redesign to revisit a Key Decision (e.g., "actually, schema-per-tenant is fine") | Low | High | The Key Technical Decisions section is the place for that conversation. Any change to a Key Decision invalidates downstream units; re-prompt before U3 if a decision flips. |
| Phase-0-redux plan (U5) ends up larger than 14-16 units when written | Med | Low | Plan size is informational, not a contract. Adjust during U5 drafting if real unit count is higher; document in U5's overview. |
| Provisioning workflow under-specified in U2, leaks into U5 | Med | Med | U2's §2 must include lifecycle states + idempotent provisioning steps + failure recovery. U5 references the section, does not re-derive it. Pre-flight check: U5 author re-reads U2 §2 before drafting U2-redux unit. |
| Migration-attribute regression guard (R9) gets watered down to "we'll add the test later" | Med | High | R9 is named as a top-level requirement, not a sub-requirement of U5. The Phase-0-redux U8-redux unit explicitly carries it. The smoke test is a per-PR gate, not nightly. |
| ReadCommitted isolation choice (R10) is wrong (the conditional INSERT actually does need SERIALIZABLE for some pattern we missed) | Low | High | Postgres docs reference (Conditional INSERT pattern, "Concurrency Control" chapter) cited verbatim in Tech Design §4. If a future load test surfaces a race, the response is to add SELECT FOR UPDATE on the stock_items row, NOT to bring back SERIALIZABLE — bracketed in §4 as "if X happens, do Y". |

---

## Documentation / Operational Notes

- This plan stays in `docs/plans/` permanently as the redesign trigger artifact. Future readers wondering "why are there two `.docx` revisions?" land here.
- The two existing plans (`2026-04-27-001-...`, `2026-05-10-001-...`) gain front-matter additions: `superseded_by: docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md`.
- `docs/CHANGELOG.md` (new) becomes the canonical "what changed when" index. Future redesigns add entries here.
- This plan does not produce a phase-gate sign-off. Sign-off lives in the implementation plans (U5, U6) once they execute. The redesign itself is "done" when U7's archival commit lands.

---

## Sources & References

- **Trigger session transcript**: 2026-05-10 / 2026-05-11 — three findings (migration silent no-op, SERIALIZABLE 40001 race, multi-tenancy pivot decision) collated by the user-AI dialogue that preceded this plan.
- **Original canon being superseded**:
  - `01-product-development-plan.md.docx` (current revision)
  - `02-technical-design-document.md.docx` (current revision)
  - `docs/adr/0001-aspire-vs-docker-compose.md`
  - `docs/adr/0002-modular-monolith-first.md`
  - `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md`
  - `docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md`
- **Captured learnings still relevant after pivot**:
  - `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`
  - All other `docs/solutions/` entries (10 from Phase-0) are tenancy-model-agnostic and remain valid.
- **External references**:
  - Postgres documentation, "Concurrency Control" chapter — conditional-INSERT-with-WHERE pattern under READ COMMITTED.
  - PDPA Vietnam (Decree 13/2023/ND-CP) — personal data definitions, breach notification, consent.
  - Singapore PDPA — data residency obligations, sub-processor disclosure.
  - PgBouncer transaction-pooling-mode documentation.
