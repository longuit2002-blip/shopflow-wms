# ShopFlow WMS — AI-Assisted Development Methodology

A chronological case study of building ShopFlow WMS — a 12-week portfolio Warehouse Management System for SEA marketplaces — using Claude Code + the compound-engineering skill cadence. Seven sprints, one solo developer, .NET 9 + Postgres + modular monolith. This doc captures what worked, what didn't, and the patterns that compounded across sprints.

---

## Table of Contents

- [Context — what this doc is, and what it's not](#context--what-this-doc-is-and-what-its-not)
- [How the project was built — chronological sprint narrative](#how-the-project-was-built--chronological-sprint-narrative)
  - [Phase-0-redux — Foundation (DB-per-tenant pivot)](#phase-0-redux--foundation-db-per-tenant-pivot)
  - [Sprint-1-redux — Reservation ledger](#sprint-1-redux--reservation-ledger)
  - [Sprint-2-redux — Inbound module](#sprint-2-redux--inbound-module)
  - [Sprint-2.5 — Cross-module outbox prefix](#sprint-25--cross-module-outbox-prefix)
  - [Sprint-3-redux — Outbound saga](#sprint-3-redux--outbound-saga)
  - [Sprint-4 — Channel webhook ingress](#sprint-4--channel-webhook-ingress)
  - [Sprint-4.5 — Webhook follow-up + scale gate](#sprint-45--webhook-follow-up--scale-gate)
  - [Sprint-5 — Stock sync engine (egress)](#sprint-5--stock-sync-engine-egress)
- [Synthesis — patterns that compounded across sprints](#synthesis--patterns-that-compounded-across-sprints)
  - [Cadence: brainstorm → plan → work → sign-off](#cadence-brainstorm--plan--work--sign-off)
  - [KTD discovery: plan-time vs mid-sprint emergence](#ktd-discovery-plan-time-vs-mid-sprint-emergence)
  - [Subagent dispatch: context isolation under pressure](#subagent-dispatch-context-isolation-under-pressure)
  - [Deferral pattern: Sprint-4 → 4.5, Sprint-5 → 5.5](#deferral-pattern-sprint-4--45-sprint-5--55)
  - [Context management: AGENTS.md / CLAUDE.md / session-resume hooks](#context-management-agentsmd--claudemd--session-resume-hooks)
- [Friction — what didn't work, what cost more than expected](#friction--what-didnt-work-what-cost-more-than-expected)
- [Forward-looking — open questions, what would be different next time](#forward-looking--open-questions-what-would-be-different-next-time)
- [Appendix — reference inventory](#appendix--reference-inventory)

---

## Context — what this doc is, and what it's not

This is a single-project case study. ShopFlow WMS is a 12-week portfolio Warehouse Management System for SEA marketplaces (Shopee, Lazada, TikTok Shop), built solo on a .NET 9 + Postgres + modular-monolith stack. Across seven sprints (Phase-0-redux through Sprint-5), the project shipped: a database-per-tenant routing foundation, an append-only reservation ledger with atomic CTE-based oversell protection, modules for inbound (PO receiving), outbound (fulfillment saga), channel ingress (marketplace webhook receivers), and channel egress (a four-layer isolation pipeline pushing stock updates back to marketplaces). The codebase is at [github.com/longuit2002-blip/shopflow-wms](https://github.com/longuit2002-blip/shopflow-wms).

The methodology was Claude Code + the compound-engineering plugin's skill cadence: `/ce-brainstorm` for product decisions, `/ce-plan` for technical decisions, `/ce-work` for execution. Persistent context lives in `AGENTS.md` and [CLAUDE.md](../CLAUDE.md); per-sprint artifacts live in `docs/brainstorms/`, `docs/plans/`, and `docs/phase-gates/`. Institutional learnings — things future-self should re-discover only once — live in [docs/solutions/](solutions/).

What this doc is not: a universal methodology claim. The patterns described worked for **one project, one solo developer, one stack, one tool combination**. They may not generalize. Specifically: solo work removes coordination overhead that a team would face; long-running project (7+ sprints) lets persistent docs amortize their cost; Claude Code's specific skill primitives shape the cadence; .NET 9 + Postgres has its own friction modes that other stacks don't. Read this as evidence, not prescription.

If you came expecting "AI saved me X% time" or "AI 10x'd my output" — this is not that doc. There are no productivity multipliers measured here. What's measured is: 7 sprints shipped to tag, 50+ commits with conventional messages, multiple emergent design decisions caught either at plan-time or mid-sprint, several friction modes that cost real time and that future projects would benefit from anticipating. The honest claim is: this methodology let one solo developer ship more rigorous architecture than they would have shipped without it, at a cost paid mostly in documentation overhead.

The reader this doc is written for: future-self, six months from now, starting a new project and trying to remember what compounded vs what wasted effort. Secondary reader: a developer who clones this repo and wants to understand how it was built without reading every sign-off doc.

---

## How the project was built — chronological sprint narrative

*Each section follows the same shape: what was built (one or two sentences), Key Technical Decisions (planned + emergent), deferrals (Skip'd slots and scope cuts), what worked, what surfaced friction, and reference links.*

### Phase-0-redux — Foundation (DB-per-tenant pivot)

**What was built.** Two-week foundation sprint (W0-W2 of the 12-week roadmap). Shipped: `ShopFlow.SharedKernel` with four Roslyn analyzers (ShopFlow0001-0004); `ShopFlow.ControlPlane` with a tenant-lifecycle aggregate and catalog DB migration; `shopflow-migrate` CLI for `provision / apply / archive / restore / status` operations; Aspire AppHost orchestrating Postgres + PgBouncer + Redis + RabbitMQ + observability; four module quartet scaffolds (Inventory + Inbound + Outbound + Channel) plus Analytics triplet and Gateway YARP scaffold; CI workflows (per-PR and chaos-nightly); `shopflow-gate phase-0-redux` operational CLI. Ten implementation units shipped; tag `v0.2.0-phase-0-redux`. Sign-off: [docs/phase-gates/2026-05-12-phase-0-redux-signoff.md](phase-gates/2026-05-12-phase-0-redux-signoff.md).

**Why this is "redux" not "Phase-0".** Phase-0-redux supersedes an earlier `v0.1.0-phase-0` that was built under the v2.0 RLS-shared multi-tenancy model. The pivot to database-per-tenant happened mid-Sprint-1 of the original Phase-1 work — a Sprint-1 integration test run on Docker surfaced three findings within one hour: (1) hand-authored EF migrations were silently no-opping, (2) the SERIALIZABLE 40001 race on conditional CTE INSERT had no caught handler, (3) PDPA SEA hard isolation requires physical tenant separation that RLS doesn't deliver. The result: [ADR-0003](adr/0003-database-per-tenant-for-compliance.md) accepted, ~2 weeks of Phase-0 plus 1 week of Sprint-1 work archived, three institutional learnings preserved at [docs/solutions/2026-05-10-ef-migration-needs-attributes.md](solutions/2026-05-10-ef-migration-needs-attributes.md), [docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md](solutions/2026-05-12-readcommitted-conditional-cte-correctness.md), and a third on FsCheck Replay format. Trigger-to-decision elapsed time was about an hour; decision-to-canon-committed about half a day.

**Key technical decisions.** Plan-time D1-D4 captured in [CLAUDE.md](../CLAUDE.md): D1 PgBouncer pool sizing (`pool_mode=transaction`, `default_pool_size=20`, Postgres `max_connections=500` dev / `1000` prod); D2 catalog cache (5 min TTL, LRU size 1000, synchronous eviction on provision/archive); D3 migration smoke-test assertion is `__ef_migrations_history` row count ≥ 1 after `MigrateAsync()` plus named-table + named-PK existence checks; D4 routing middleware priority is header > JWT > subdomain with a 2+ source conflict raising 403 plus audit row. The `[Migration]` + `[DbContext]` attribute requirement on hand-authored migrations is canon because the v2.0 silent no-op was the trigger that broke the prior phase.

**Deferrals.** Aspire cold-start measurement and provisioning latency p99 deferred to a Docker-enabled session — the dev machine had Docker Desktop installed but the daemon wasn't running. CI captures the numbers; sign-off documents the deferral honestly. CSharpier formatting cleanup on 23 inherited drift files deferred to a follow-up commit.

**What worked.** Test-first cadence applied to U4 SharedKernel analyzers caught analyzer regressions before code review. The `shopflow-migrate` CLI's `MigrateAsync()` smoke test was load-bearing — it would have caught the v2.0 silent no-op directly. The 10-unit cadence with sign-off-at-close (U10) became the template for every subsequent sprint.

**Friction.** Pre-existing CSharpier formatting drift on 23 files inherited from U4-U6 commits means CI's `csharpier --check` step blocks on first run — one cleanup commit unblocks but the noise is real. The Aspire MSBuild SDK requirement (`<Sdk Name="Aspire.AppHost.Sdk" Version="13.3.0" />`) for .NET 9 wasn't obvious — without it `dotnet build` raises NETSDK1147; documented in the sign-off so future Aspire bumps don't re-hit it. The `Microsoft.Extensions.Hosting` bump from 9.0.0 to 10.0.7 (forced by Aspire 13.3.0's transitive floor) crossed major versions and required cross-targeting verification — a "yes my Aspire upgrade is also a runtime-floor upgrade" surprise.

### Sprint-1-redux — Reservation ledger

**What was built.** The reservation ledger — the hot-path correctness primitive that prevents oversell at flash-sale scale. Shipped: `ReservationRepository.TryReserveAsync` with a conditional-CTE INSERT at READ COMMITTED isolation (the v3.0 correction over v2.0's SERIALIZABLE); `23505` UNIQUE-violation catch for idempotent retry behaviour; `StockReservedEvent` outbox emission inside the same transaction; `Confirm` / `Release` / `ReleaseExpired` paths; multiplexed `ReservationExpiryWorker` BackgroundService that fans out across `Ready` tenants per `InventoryOptions.ExpiryPollIntervalSeconds`; `ShopFlow.PropertyTests` with FsCheck properties (HappyPathConcurrency, StrictCapacity, Idempotency, ExpiryReleasesActiveRows, InvariantHoldsForAnyOperationSequence) against a real Postgres fixture; `MultiTenantScaleGateTests` (5 tenants × 1000 reservations) with fairness floor measurement. Six units; tag `v0.3.0-sprint-1-redux`. Sign-off: [docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md](phase-gates/2026-05-12-sprint-1-redux-signoff.md).

**Key technical decisions.** The READ COMMITTED correction is the headline. The v2.0 design used SERIALIZABLE with conditional CTE; the v3.0 redesign documented that this pairs incorrectly — SERIALIZABLE raises 40001 on the second commit, which is a retry signal, not a correctness signal. READ COMMITTED with the predicate inside the UPDATE itself (`WHERE available >= @needed`) serialises concurrent writes correctly because Postgres locks the row during UPDATE. The institutional learning at [docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md](solutions/2026-05-12-readcommitted-conditional-cte-correctness.md) captures the proof. A secondary KTD: the per-tenant DbContext flows through `IRequestContext.DbConnectionString` in a request-scoped factory, and the `ReservationRepository` takes the bound DbContext directly rather than going through a per-request open-generic factory — the open-generic factory plumbing in `AddShopFlowDefaults` is preserved for any future per-message dispatcher path that opens its own scope.

**Deferrals.** Scale-gate runtime measurement deferred initially because the dev machine's Docker daemon wasn't running. A subsequent measurement on a Docker-enabled session captured 5×1000 reservations with p99 of 18.4-20.6 seconds per tenant, fairness floor 0.877/0.895, and zero oversells across 5000 operations. The p99 is dev-hardware-bound, not architecture-bound; CI on Linux re-validates against the absolute target. The honest framing in the sign-off says "throughput target is production-hardware-bound" rather than claiming the dev number is the production number.

**Friction.** U4 property tests promised "zero test-body edits when the port pivots" — that was the original sales pitch of FsCheck-against-stub. In practice the port pivoted twice (U2 add of `FindByOrderIdAsync`, U8 add of `IRequestContext`-aware constructor) and the test bodies had to be re-derived. The properties' invariants survived; the call sites did not. The honest revision: "FsCheck properties are stable in *intent*, not in *call shape*". Property 5 (`InvariantHoldsForAnyOperationSequence`) wanted a read-back surface — `GetActiveSumAsync` / `GetConfirmedSumAsync` — that the port didn't yet expose; Property 5 reads the ledger directly via raw SQL as a documented stop-gap. Sprint-2-redux would later open the read-back surface when Inbound needed it; the property never swapped to use it. That's honest scope cut, not solved problem.

**What worked.** Test-first cadence in U1 caught a subtle race in the conditional CTE before it shipped. The atomic-fail rollback path — when zero rows insert because of oversell, roll back so the outcome computation reads actual committed availability — was implemented because a previous version mixed partial commits with the outcome computation and returned wrong oversold-line lists. The integration test driving 100 concurrent reservations against `available=10` caught it. The scale gate's per-tenant fairness measurement (min push / max push) became the template for every subsequent multi-tenant scale gate (Sprint-3-redux Outbound, Sprint-4.5 webhook, Sprint-5 stock-sync).

### Sprint-2-redux — Inbound module

*(Section body lands in U3.)*

### Sprint-2.5 — Cross-module outbox prefix

*(Section body lands in U3.)*

### Sprint-3-redux — Outbound saga

*(Section body lands in U3.)*

### Sprint-4 — Channel webhook ingress

*(Section body lands in U4.)*

### Sprint-4.5 — Webhook follow-up + scale gate

*(Section body lands in U4.)*

### Sprint-5 — Stock sync engine (egress)

*(Section body lands in U5.)*

---

## Synthesis — patterns that compounded across sprints

*(Section body lands in U6 — pattern catalog with 5 subsections plus 1-2 Mermaid diagrams.)*

### Cadence: brainstorm → plan → work → sign-off

### KTD discovery: plan-time vs mid-sprint emergence

### Subagent dispatch: context isolation under pressure

### Deferral pattern: Sprint-4 → 4.5, Sprint-5 → 5.5

### Context management: AGENTS.md / CLAUDE.md / session-resume hooks

---

## Friction — what didn't work, what cost more than expected

*(Section body lands in U7 — 6+ named friction modes with Pattern / Cost / Mitigation framing.)*

---

## Forward-looking — open questions, what would be different next time

*(Section body lands in U7 — open questions, process improvements that would not be in scope but are worth surfacing for project sau.)*

---

## Appendix — reference inventory

All artifacts the chronological narrative and synthesis sections reference. Grouped by category.

### Brainstorms — `docs/brainstorms/`

- [2026-05-12-sprint-2-redux-inbound-requirements.md](brainstorms/2026-05-12-sprint-2-redux-inbound-requirements.md) — Sprint-2-redux Inbound module scope (PurchaseOrder + Receiving + cross-module outbox).
- [2026-05-13-sprint-3-redux-outbound-requirements.md](brainstorms/2026-05-13-sprint-3-redux-outbound-requirements.md) — Sprint-3-redux Outbound module scope (saga + picking + shipping).
- [2026-05-14-sprint-4.5-webhook-followup-requirements.md](brainstorms/2026-05-14-sprint-4.5-webhook-followup-requirements.md) — Sprint-4.5 closure scope (4 Sprint-4 deferrals).
- [2026-05-16-sprint-5-stock-sync-requirements.md](brainstorms/2026-05-16-sprint-5-stock-sync-requirements.md) — Sprint-5 Stock Sync Engine scope (4-layer isolation pipeline).
- [2026-05-16-sprint-5-visual.html](brainstorms/2026-05-16-sprint-5-visual.html) — Sprint-5 visual companion when prose dialogue got confusing.
- [2026-05-18-methodology-writeup-requirements.md](brainstorms/2026-05-18-methodology-writeup-requirements.md) — this writeup's own origin doc.

### Plans — `docs/plans/`

- [2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md](plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md) — the plan-of-plans for the DB-per-tenant pivot.
- [2026-05-11-002-phase-0-redux-bootstrap-plan.md](plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md) — Phase-0-redux foundation plan.
- [2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) — Sprint-1-redux reservation ledger plan.
- [2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md](plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md) — Sprint-2-redux Inbound plan.
- [2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md](plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md) — Sprint-3-redux Outbound plan.
- [2026-05-13-003-feat-phase-2-sprint-4-channel-webhook-plan.md](plans/2026-05-13-003-feat-phase-2-sprint-4-channel-webhook-plan.md) — Sprint-4 Channel webhook plan.
- [2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md](plans/2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md) — Sprint-4.5 webhook follow-up plan.
- [2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md](plans/2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md) — Sprint-5 Stock Sync Engine plan.
- [2026-05-18-001-feat-methodology-writeup-plan.md](plans/2026-05-18-001-feat-methodology-writeup-plan.md) — this writeup's own plan.

### Sign-offs — `docs/phase-gates/`

- [2026-05-12-phase-0-redux-signoff.md](phase-gates/2026-05-12-phase-0-redux-signoff.md) — Phase-0-redux completion.
- [2026-05-12-sprint-1-redux-signoff.md](phase-gates/2026-05-12-sprint-1-redux-signoff.md) — Sprint-1-redux completion.
- [2026-05-13-sprint-2-redux-signoff.md](phase-gates/2026-05-13-sprint-2-redux-signoff.md) — Sprint-2-redux completion.
- [2026-05-13-sprint-2.5-signoff.md](phase-gates/2026-05-13-sprint-2.5-signoff.md) — Sprint-2.5 closure (cross-module outbox prefix).
- [2026-05-13-sprint-3-redux-signoff.md](phase-gates/2026-05-13-sprint-3-redux-signoff.md) — Sprint-3-redux completion.
- [2026-05-13-sprint-4-signoff.md](phase-gates/2026-05-13-sprint-4-signoff.md) — Sprint-4 completion.
- [2026-05-15-sprint-4.5-signoff.md](phase-gates/2026-05-15-sprint-4.5-signoff.md) — Sprint-4.5 closure (4 Sprint-4 deferrals).
- [2026-05-17-sprint-5-signoff.md](phase-gates/2026-05-17-sprint-5-signoff.md) — Sprint-5 completion.

### Architectural Decision Records — `docs/adr/`

- [0001-aspire-vs-docker-compose.md](adr/0001-aspire-vs-docker-compose.md) — Aspire for dev orchestration, Docker Compose for prod.
- [0002-modular-monolith-first.md](adr/0002-modular-monolith-first.md) — modular monolith first, microservice split deferred.
- [0003-database-per-tenant-for-compliance.md](adr/0003-database-per-tenant-for-compliance.md) — PDPA SEA hard isolation drives DB-per-tenant.

### Institutional learnings — `docs/solutions/`

- [2026-05-10-ef-migration-needs-attributes.md](solutions/2026-05-10-ef-migration-needs-attributes.md) — hand-authored EF migrations silently no-op without `[Migration]` + `[DbContext]` attributes.
- [2026-05-12-readcommitted-conditional-cte-correctness.md](solutions/2026-05-12-readcommitted-conditional-cte-correctness.md) — READ COMMITTED + conditional-CTE INSERT correctness (v3.0 correction to v2.0 SERIALIZABLE).
- [2026-05-13-cross-module-outbox-table-name-collision.md](solutions/2026-05-13-cross-module-outbox-table-name-collision.md) — Sprint-2-redux U9 finding that drove Sprint-2.5 closure.
- [2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md](solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md) — EF Core 9 `PendingModelChangesWarning` mitigation for hand-authored migrations.
- [2026-05-13-multi-row-cte-predicate-must-live-in-update.md](solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md) — Sprint-3-redux K11 — multi-row CTE concurrency, predicate must live in UPDATE, not in pre-check.

### Git tags

| Tag | Date | Scope |
|---|---|---|
| `v0.1.0-phase-0` | (archived) | Original Phase-0 work, superseded by Phase-0-redux pivot. |
| `v0.2.0-phase-0-redux` | 2026-05-12 | Phase-0-redux foundation (DB-per-tenant + ControlPlane + Aspire + migrate CLI). |
| `v0.3.0-sprint-1-redux` | 2026-05-12 | Sprint-1-redux reservation ledger. |
| `v0.4.0-sprint-2-redux` | 2026-05-13 | Sprint-2-redux Inbound module + RabbitMQ flip. |
| `v0.4.1-sprint-2.5` | 2026-05-13 | Sprint-2.5 cross-module outbox prefix. |
| `v0.5.0-sprint-3-redux` | 2026-05-13 | Sprint-3-redux Outbound saga + scale gate. |
| `v0.6.0-sprint-4` | 2026-05-13 | Sprint-4 Channel webhook ingress. |
| `v0.6.1-sprint-4.5` | 2026-05-15 | Sprint-4.5 webhook follow-up + 3 scale-gate bodies. |
| `v0.7.0-sprint-5` | 2026-05-17 | Sprint-5 Stock Sync Engine (4-layer isolation pipeline). |
| `v0.8.0-methodology-writeup` | 2026-05-18 | This methodology writeup (no source code changes). |

---

*Snapshot dated 2026-05-18. Future-self updates this doc when new patterns surface or old patterns turn out wrong.*
