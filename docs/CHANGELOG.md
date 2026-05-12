# ShopFlow WMS — Canon Supersession History

This file records architectural decisions that change the foundational shape of the system. ADRs land in `docs/adr/`; this file is the thin index pointing at them with the date and trigger context. Implementation-level changes live in commits and `docs/solutions/`, not here.

---

## 2026-05-11 — Multi-tenancy pivot: RLS-shared → Database-per-tenant

**Trigger**: Phase-1 Sprint-1 integration test run on Docker host surfaced three findings within one hour:

1. Hand-authored EF migration silent no-op (missing `[Migration]` + `[DbContext]` attributes) — captured in [docs/solutions/2026-05-10-ef-migration-needs-attributes.md](solutions/2026-05-10-ef-migration-needs-attributes.md).
2. SERIALIZABLE 40001 race on conditional CTE INSERT — repository code did not catch; W3 scale gate's premise broke.
3. User compliance lens: PDPA SEA hard isolation requires physical tenant separation; RLS is a logical guarantee, weaker under audit scrutiny than DB-per-tenant.

**Decision**: [ADR-0003](adr/0003-database-per-tenant-for-compliance.md) — database-per-tenant on shared Postgres cluster. Compliance anchor: **PDPA Vietnam + Singapore PDPA**. Scale anchor: **25-50 validated tenants on single cluster**. Routing: per-request via middleware. PgBouncer in transaction-pooling mode is non-optional infrastructure.

**Supersedes**:
- v2.0 of `01-product-development-plan.md.docx` and `02-technical-design-document.md.docx` (the canon assumed RLS-from-day-1 single-tenant MVP)
- ADR-0001 + ADR-0002 carry postscripts noting the "RLS-as-cheapest-decision" claim is superseded; the ADRs themselves stand
- AGENTS.md §3 rewritten (7 RLS rules → 7 routing-and-catalog rules)
- `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` (Phase-0 plan v2.0) — superseded by [Phase-0-redux plan](plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md)
- `docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md` (Sprint-1 plan v2.0) — superseded by [Sprint-1-redux plan](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md)

**New canon**:
- [docs/redesign/01-product-development-plan.md](redesign/01-product-development-plan.md) v3.0
- [docs/redesign/02-technical-design-document.md](redesign/02-technical-design-document.md) v3.0
- [ADR-0003](adr/0003-database-per-tenant-for-compliance.md)
- [docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md](plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md) — the plan-of-plans

**Archive references**:
- Branch: `archive/phase-1-sprint-1-rls-shared` (was `feat/phase-1-sprint-1`)
- Tag: `archive/v0.1.0-phase-0-rls-shared` (annotated supersession note attached to the original `v0.1.0-phase-0` commit)

**Implementation branch** (active): `feat/phase-0-redux-db-per-tenant`

**Cost of pivot**: ~2 weeks of Phase-0 work + 1 week of Sprint-1 work-in-progress thrown away. Three learnings preserved (EF migration attributes, FsCheck Replay gamma format, green-against-stub property pattern). Trigger-to-decision elapsed time: ~1 hour. Decision-to-canon-committed elapsed time: ~half a day.

---

## 2026-05-12 — Phase-0-redux complete

**Tag**: [`v0.2.0-phase-0-redux`](https://github.com/longuit2002-blip/shopflow-wms/releases/tag/v0.2.0-phase-0-redux). Closes [Phase-0-redux plan](plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md) U1-U10 on branch `feat/phase-0-redux-db-per-tenant`. Sign-off doc: [docs/phase-gates/2026-05-12-phase-0-redux-signoff.md](phase-gates/2026-05-12-phase-0-redux-signoff.md).

**Shipped**:
- DB-per-tenant foundation per [ADR-0003](adr/0003-database-per-tenant-for-compliance.md): SharedKernel (`IRequestContext`, `IDbContextFactory<T>`, `ITenantCatalog`, `OutboxDispatcher`, `TenantRoutingMiddleware`), ControlPlane catalog with mandatory-attribute migration, `shopflow-migrate` per-tenant runner CLI.
- Aspire AppHost wiring Postgres + PgBouncer (transaction pooling) + Redis + RabbitMQ + observability stack (Seq, Tempo, otel-collector, Prometheus, MinIO); chained bootstrap provisions `shopflow_control` + dev1 + dev2 before any service starts. Production handoff in `infrastructure/docker-compose.yml`.
- Inventory module (schema-only blessed reference) with the reservation-ledger schema locked: `UNIQUE(order_id)` idempotency anchor, `xid` row_version, no `tenant_id` on business tables. Repository methods throw `NotImplementedException("Sprint-1-redux …")` — the W1 green-against-stub state.
- 4 module shape replicas (Inbound/Outbound/Channel quartets, Analytics triplet) + Gateway YARP scaffold; per-module AGENTS.md ≤ 50 lines each.
- 4 ShopFlow Roslyn analyzers locked at Error: no raw DbSet, no `IPublishEndpoint.Publish` mid-transaction, no DbContext instantiation outside factory, no `DateTime.Now`.
- CI workflows: per-PR (build + csharpier + unit + Testcontainers migration smoke + cross-tenant routing); nightly chaos (integration + property + load + chaos placeholders).
- Operational `shopflow-gate phase-0-redux` CLI: catalog reachable, catalog migrated, all tenants Ready, PgBouncer reachable.

**Carried forward as canon**: docs/solutions/2026-05-10-ef-migration-needs-attributes.md (codified into `MigrationSmokeTests`).

**Deferred** (documented in sign-off): Aspire cold-start measurement and provisioning latency p99 (need Docker on the dev machine); CSharpier formatting cleanup of 23 files inherited from U4-U6; Inventory repository behavior (Sprint-1-redux); channel adapters + mock servers (Phase-2 Sprint-4); PgBouncer HA pair (Phase-2); tenant onboarding UI (Phase-3).

**Next**: [Sprint-1-redux reservation ledger plan](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) cuts from this tag.

---

## 2026-05-12 — Phase-1 Sprint-1-redux complete

**Tag**: `v0.3.0-sprint-1-redux`. Closes [Sprint-1-redux plan](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) U1-U6 on branch `feat/phase-1-sprint-1-redux-reservation-ledger`. Sign-off doc: [docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md](phase-gates/2026-05-12-sprint-1-redux-signoff.md).

**Shipped**:
- `ReservationRepository` hot path: `TryReserveAsync` ships the conditional-CTE INSERT pattern at READ COMMITTED — the UPDATE on `stock_items` serialises contention via the row lock; the INSERT into `reservations_ledger` is gated on the UPDATE producing a row. Idempotency layered: app-level short-circuit via `FindByOrderIdAsync` + DB-level `UNIQUE(order_id)` with `23505` catch-and-refetch.
- Full `IReservationRepository` surface: `FindByOrderIdAsync`, `ConfirmAsync` (with NOT_FOUND / ALREADY_CONFIRMED / INVALID_STATE codes), `ReleaseAsync`, `ReleaseExpiredAsync` (multi-CTE batched UPDATE + outbox-per-row). Domain methods on `Reservation` and `StockItem` filled in for the same state machines on non-hot paths.
- Multiplexed `ReservationExpiryWorker` — one BackgroundService visits every `Ready` tenant per tick; per-tenant scope binds `RequestContext` before resolving the repository; per-tenant exception isolation keeps healthy tenants progressing.
- `ShopFlow.Inventory.IntegrationTests` (14 tests) — `ReservationRepositoryTests` covering happy path, exact-available, oversold, idempotency, concurrent oversell, FindByOrderId, Confirm, Release, ReleaseExpired; `ReservationExpiryWorkerTests` covering construction validation, single-tenant tick, multi-tenant fan-out, broken-tenant isolation; `MultiTenantScaleGateTests` (the W3 5×1000 fairness floor gate).
- `ShopFlow.PropertyTests` — 5 FsCheck properties on the reservation ledger (`HappyPathConcurrency_AllSucceed`, `StrictCapacity_NoOversell`, `Idempotency_OneUniqueId`, `ExpiryReleasesActiveRows`, `InvariantHoldsForAnyOperationSequence`) wired to a real `ReservationRepository` via the `ReservationRepositoryHandle` static-slot pattern.
- `InventoryOptions` config surface for the expiry worker (`ExpiryPollIntervalSeconds`, `ExpiryBatchSize`, `DefaultReservationTtlMinutes`).

**New compounding learning**: [docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md](solutions/2026-05-12-readcommitted-conditional-cte-correctness.md) — captures the SERIALIZABLE→ReadCommitted decision rationale so the next conditional-write surface doesn't re-derive.

**Deferred** (documented in sign-off): Docker-backed measurement of W3 scale-gate p99 and fairness floor (Docker daemon not running this session); `GetActiveSumAsync` / `GetConfirmedSumAsync` read-back surface (Sprint-2-redux); multi-instance expiry worker leader election (Phase-2); `StockItemRepository` behavior (Sprint-2-redux for Inbound's GRN flow); NBomber promotion of the load harness; CSharpier formatting cleanup (carried).

**Next**: Sprint-2-redux (Inbound module W4) cuts from `v0.3.0-sprint-1-redux`.

---
