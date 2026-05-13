# ShopFlow WMS — Project Context for AI Assistants

This file is auto-loaded by Claude Code (and respected as a fallback by other agents). It captures the project context that should ship with the source. The user works across multiple computers — anything project-related belongs here in the source tree, not in machine-local memory.

## What this project is

**ShopFlow WMS** — 12-week single-developer portfolio Warehouse Management System for SEA marketplaces (Shopee, Lazada, TikTok Shop, Shopify). Source is being bootstrapped from scratch as of late April 2026.

**Stack**: C# .NET 8, Next.js 14 App Router + React Query + SignalR, Postgres 16, **PgBouncer** (transaction-pooling), Redis, RabbitMQ, MassTransit (sagas + outbox), OpenTelemetry, Aspire AppHost (dev) + hand-maintained Docker Compose (prod). Six modular-monolith microservices internally: Gateway (YARP), Inventory, Inbound, Outbound, Channel, Analytics.

**Engineering anchors**:
- **Database-per-tenant** on shared Postgres cluster ([ADR-0003](./docs/adr/0003-database-per-tenant-for-compliance.md)) — PDPA SEA hard isolation. Routing per-request via middleware; control-plane catalog DB; right-to-erasure is `DROP DATABASE`.
- Append-only **reservation ledger** (CTE-based conditional INSERT at READ COMMITTED, not row lock) — the hot-key flash-sale solution.
- **Stock sync engine** with coalescing buffer + per-channel token bucket + priority queue, scoped per-tenant.
- Persistent **webhook idempotency** via Postgres `UNIQUE(channel_id, provider_event_id)` per tenant DB.
- **Outbox pattern** per-tenant with multiplexed dispatcher; dispatcher path: polling → LISTEN/NOTIFY → Debezium CDC at scale.
- **MassTransit saga** for fulfillment orchestration (Reserve → Pick → Pack → Ship with compensation), tenant context per-message via headers.

## Source documents (canonical)

- [01-product-development-plan.md.docx](./01-product-development-plan.md.docx) — v2.0 (April 2026); v3.0 markdown draft at [docs/redesign/01-product-development-plan.md](./docs/redesign/01-product-development-plan.md). The .docx will be regenerated from the v3.0 draft.
- [02-technical-design-document.md.docx](./02-technical-design-document.md.docx) — v2.0; v3.0 markdown draft at [docs/redesign/02-technical-design-document.md](./docs/redesign/02-technical-design-document.md). Same regeneration plan.

These are .docx (Word) files. To extract text for grep/search, run [tools/extract-docs.sh](./tools/extract-docs.sh) (bash) or [tools/extract-docs.ps1](./tools/extract-docs.ps1) (PowerShell). The script writes plain-text equivalents to `docs/source/` (gitignored) — re-runnable on any machine.

**v3.0 changes summary**: multi-tenancy is now §1 (was §4). Tenancy model is database-per-tenant on shared Postgres cluster ([ADR-0003](./docs/adr/0003-database-per-tenant-for-compliance.md)). The reservation ledger conditional CTE INSERT runs at READ COMMITTED (was SERIALIZABLE in v2.0 — corrected per Postgres docs). Outbox is per-tenant with multiplexed dispatcher.

## Bootstrap stance (decided 2026-04-27)

Per [docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md](./docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md), Phase 0-1 ships **ONE container as a modular monolith** with six logical modules in separate `.csproj` per bounded context. Mechanical split into 6 microservice processes is a planned **W6 event** when the channel adapter framework arrives and async cross-process messaging actually pays its freight. README opens with the eventual 6-service diagram and labels Phase 0-1 as "modular monolith stage."

Top-7 bootstrap ideas captured in the ideation doc above. Recommended W0 / W1 / W2 sequence is in that file's "Recommended Bootstrap Sequence" section.

## Hard non-negotiables (from the design)

- **Correctness over latency.** Oversell is a correctness bug, not a performance bug. Reject ambiguous orders rather than queuing optimistically.
- **Idempotency everywhere.** Every consumer, webhook receiver, external-API call must be idempotent.
- **Multi-tenancy from day 1.** `tenant_id` on every row + RLS policies even at MVP single-tenant. The cheapest scale decision in the whole design (Tech Design §4.5).
- **Observability built in Phase 0**, not retrofitted. Correlation ID + W3C TraceContext propagated through every service.
- **No cloud lock-in.** Docker Compose for dev; production path is plain containers on any orchestrator.

## Working preferences

- **Cross-machine workflow.** User zips and ships the source between computers. Anything project-related — context, scripts, decisions, ideation, ADRs — must live inside this directory tree, not in `~/.claude/projects/...` or other machine-local locations.
- **Source docs are .docx**, not markdown (with v3.0 markdown drafts in `docs/redesign/` pending .docx regeneration). Read via the extraction scripts under `tools/`. Treat the .docx as the source of truth; do not edit the extracted .txt as if they were originals.
- **Compounding learnings**: when a fix is non-obvious, capture in [`docs/solutions/`](./docs/solutions/). The "every reviewer comment is a missing rule" pattern is canon (AGENTS.md).

## Current stage

**Multi-tenancy redesign accepted (2026-05-11)**. Phase-0 (RLS-shared) and Phase-1 Sprint-1 work-in-progress are archived at `archive/phase-1-sprint-1-rls-shared` branch and `archive/v0.1.0-phase-0-rls-shared` tag. The system pivots to **database-per-tenant on shared Postgres cluster** under [ADR-0003](./docs/adr/0003-database-per-tenant-for-compliance.md).

**Active branch**: `feat/phase-1-sprint-3-redux-outbound` (cut from `v0.4.1-sprint-2.5`).

**Sprint-3-redux is complete.** Tag: `v0.5.0-sprint-3-redux`. Sign-off: [`docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md`](./docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md). **Closes Phase-1's customer funnel** — Inventory holds stock (Sprint-1-redux), Inbound fills it (Sprint-2-redux), Outbound drains it (Sprint-3-redux). The Outbound module ships the full fulfillment saga (MassTransit state machine, 11 states, EF saga repository with K12 per-tenant DbContext binding), 9 cross-module contracts, 3 new Inventory consumers wrapping the extended `ReservationRepository` (`TryReserveLinesAsync` + `ReleaseLinesAsync`), `IPickQueue` per-tenant `Channel<PickRequestV1>` + `PickWaveGeneratorService`, mocked shipping carrier with Polly v8 retry, pick-failure compensation, and the W5 scale gate (operator-pipeline measurement). K11 multi-row CTE concurrency fix landed as institutional learning. K15 MT.EFCore 8.3.4 + EF Core 9 binding verified.

**Sprint-3-redux progress** (as of 2026-05-13):
- ✅ U1 — Outbound module quartet scaffold + `InitialOutboundSchema` 7-table migration + K15 MT.EFCore 8.3.4 + EF Core 9 smoke build PASS
- ✅ U2 — `Order` + `OrderLine` aggregate + repository + `IUnitOfWork` + `IOutboundOutbox` + idempotent `POST /orders` + `GET /orders/{id}` (29 Order unit + 5 OrderRepository + 8 OrdersController integration tests)
- ✅ U3 — Inventory schema extension (`reservations_ledger` + `order_line_id` + composite UNIQUE); `IReservationRepository` gains `TryReserveLinesAsync` (atomic multi-row CTE) + `ReleaseLinesAsync`; 9 cross-module contracts (`OrderPlacedV1`, `TrackingPushedV1`, `ReserveStockV1`, `ConfirmStockV1`, `ReleaseStockV1`, `StockReservedV1`, `StockReservationFailedV1`, `StockConfirmedV1`, `StockReleasedV1`); 3 Inventory consumers (ReserveStock / ConfirmStock / ReleaseStock). **K11 CTE concurrency defect caught + fixed** (predicate must live inside UPDATE, not in pre-check CTE — see `docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md`).
- ✅ U4 — `FulfillmentSaga` state machine (11 states) + EF saga repository on `saga_state` + **K12 per-tenant DbContext binding** via `TenantBindingSagaFilter<T>` (primary path) + `TenantAwareSagaDbContextFactory<FulfillmentSagaState>` (registered fallback); test-first cadence caught MT 8.x publish-DSL trap
- ✅ U5 — `IPickQueue` per-tenant `Channel<PickRequestV1>` (bounded 1000) + `PickWaveGeneratorService` (PeriodicTimer 30s tick; 15-min window batching by `(tenant_id, shipping_profile)`; round-robin picker via deterministic cursor); saga `Reserved` Then-handler writes PickRequest + chains TransitionTo(AwaitingPick)
- ✅ U6 — `confirm-pick` + `confirm-pack` (with weight-warning) + `confirm-ship` endpoints (501 stubs replaced); `IMockShippingProvider` (1-3s delay + 5% transient-fail + Polly v8 `ResiliencePipelineBuilder` retry); `ChannelTrackingConsumer` stub; AE5/AE6/AE7 covered (14 PackShipEndpointTests + 9 MockShippingProviderTests + 2 ChannelTrackingConsumerTests)
- ✅ U7 — `mark-pick-failed` endpoint + saga `CompensatingReservation` body (Path A atomic-fail empty-set short-circuit; Path B pick-fail publish `ReleaseStockV1`); Set-based dedup on `StockReleasedV1` via `ReleasedLineSkus` HashSet; `OrderCancelledConsumer` propagates saga terminal state to Order row (7 unit + 3 integration tests)
- ⚠️ U8 — `MultiTenantOutboundScaleGateTests` (2 tests, `Category=Load`): 2000 orders × 3 tenants. **Saga path bypassed** — operator-pipeline measurement only. Dev-laptop Shipped p99 247-332ms, Cancelled p99 112-131ms, fairness floor 0.918-0.979 (all well within R17 targets and ≥ 0.85 threshold). Real-saga-throughput-under-load is a Phase-2 production-CI measurement gap.
- ✅ U9 — Per-PR integration tests close U8's saga-bypass gap: `SagaHappyPathTests` (2) + `SagaCompensationFlowTests` (2) + `CrossModuleReservationFlowTests` (2 — real both-modules-one-DB round-trip) + `PickWaveBatchingFlowTests` (1). 7 tests in ~3s.
- ✅ U10 — [Sprint-3-redux sign-off](./docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md); CHANGELOG entry; README + CLAUDE update; tag `v0.5.0-sprint-3-redux`

**Next implementation step**: cut a fresh branch from `v0.5.0-sprint-3-redux` and start **Phase-2 Sprint-4** (Channel Connections + webhook idempotency). K13 envelope-type → endpoint routing in `OutboxDispatcher` is a Phase-2 prerequisite for the W6 mechanical split.

**Sprint-3-redux deviations from plan file list**:
- **K11 CTE concurrency correction (U3)**: plan pseudocode's `will_succeed` pre-check CTE was unsafe under READ COMMITTED — caught by Sprint-1-redux's existing concurrent-oversell test. Corrected pattern (predicate in UPDATE + `all_succeeded` NOT-EXISTS gate) shipped + documented as institutional learning. Plan K11 prose updated.
- **U8 saga bypass**: scale gate's auto-driver writes `Order.status` directly instead of routing through the saga's OrderPlacedV1 → ReserveStockV1 → StockReservedV1 → AwaitingPick chain. Measures HTTP+DB-write throughput, not full saga throughput. Saga correctness gated by U4/U7/U9 integration tests; full-saga-under-load deferred to production CI.
- **U8 mock-carrier delay shortened** (5-20ms vs production 1-3s) for bounded scale-gate wall-time; real-delay path covered by `MockShippingProviderTests` at unit scale.
- **U8 warm-up phase + `NpgsqlConnection.ClearAllPools()` between tests** for repeatable runs (Postgres `max_connections=100` cap with 3 tenants).
- **U9 PickWaveBatchingFlowTests seeds PickRequests directly** instead of driving 50 sagas (45s → 383ms on dev hardware); AE4 invariant unchanged.
- **U1 saga_state inlined extension**: U4's per-state context columns (`tenant_id`, `shipping_profile`, `line_count`, `reserved_line_skus`, etc.) added inline to the U1 migration (not a follow-on migration). Safe because migration hadn't tagged or applied anywhere yet.
- **MT 8.x publish DSL**: `Publish(ctx => new T(...))` works inside `Initially`; `PublishAsync(ctx.Init<T>(new {...}))` silently fails. Caught by test-first cadence in U4.
- **K13 W6 deferral**: `OutboxDispatcher.Publish`-for-commands accepted as Sprint-3-redux trade-off (modular monolith). W6 split needs envelope-type → endpoint routing — Phase-2 prerequisite tracked.

---

**Sprint-2.5 history** (kept for context; tag `v0.4.1-sprint-2.5`). Sign-off: [`docs/phase-gates/2026-05-13-sprint-2.5-signoff.md`](./docs/phase-gates/2026-05-13-sprint-2.5-signoff.md). Closes the Sprint-2-redux U9 deferral: per-module outbox table-name prefix (`inbound_outbox_messages` / `inventory_outbox_messages`) unblocks single-physical-tenant-DB cross-module flow. Two cross-module flow integration tests landed against shared Testcontainers Postgres. Surfaced + fixed a latent JSON-options bug in the dispatcher pipeline (camelCase serialise vs case-sensitive deserialise) via `OutboxJsonOptions.Default` in SharedKernel.

**Sprint-2.5 progress** (as of 2026-05-13):
- ✅ U1 — Inbound `outbox_messages` → `inbound_outbox_messages` (entity config + migration + smoke test)
- ✅ U2 — Inventory `outbox_messages` → `inventory_outbox_messages` (entity config + Phase-0-redux U8 migration edited in-place + smoke test + raw-SQL test fixtures)
- ✅ U3 — `InboundToInventoryFlowTests` (2 tests) lands against single shared tenant DB; `ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default` centralises JSON options across 4 call sites (OutboxInterceptor, MultiplexedOutboxDispatcher, InboundOutbox, ReservationRepository)
- ✅ U4 — [Sprint-2.5 sign-off](./docs/phase-gates/2026-05-13-sprint-2.5-signoff.md); CHANGELOG + tag `v0.4.1-sprint-2.5`

---

**Sprint-2-redux history** (kept for context; tag `v0.4.0-sprint-2-redux`). Sign-off: [`docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md`](./docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md). The Inbound module + Inventory bin/zone schema extension + MassTransit RabbitMQ transport flip (W6 → W4) ship together; the first cross-module write flow (Inbound → Inventory via `ShopFlow.Contracts.Inbound.InboundConfirmedV1`) is wired end-to-end at the service + consumer level.

**Sprint-2-redux progress** (as of 2026-05-13):
- ✅ U1 — Inbound module quartet scaffold (Domain/Application/Infrastructure/Api) + 6-table `InitialInboundSchema` migration
- ✅ U2 — `PurchaseOrder` aggregate state machine + repository + 18 Domain unit tests + 4 integration tests
- ✅ U3 — `Receiving` + `ReconciliationTicket` aggregates + `ConfirmReceivingLineService` orchestrator + 6 integration tests
- ✅ U4 — Inventory schema extension: `zones`, `bins`, `stock_item_bins`, `home_zone_id`, `inbound_dedup` tables + entity configs + ports + repos
- ✅ U5 — Bin-aware `StockItemRepository.AdjustAtBinAsync` (auto-create + upsert + audit in one ReadCommitted transaction) + `PutAwaySuggestionService` (top-K ranking) + put-away controller + 8 integration tests
- ✅ U6 — `ShopFlow.Contracts.Inbound.InboundConfirmedV1` cross-module event + `IInboundOutbox` explicit-write port + `InboundConfirmedConsumer` (Inventory side, idempotent via `inbound_dedup`) + 3 consumer integration tests
- ✅ U7 — MassTransit `MessageBusTransport` enum + config switch + `AddShopFlowDefaults` wired in Inbound.Api + Inventory.Api Program.cs + ADR-0002 W6 → W4 postscript
- ✅ U8 — `PurchaseOrdersController` thin endpoints (POST/GET/PATCH for PO + POST /receive for the ConfirmReceivingLineService call)
- ⚠️ U9 — Single-tenant-DB cross-module flow test **deferred**; surfaced an architecture finding (both modules' migrations create `outbox_messages` in `public` schema → collision when sharing a tenant DB). Documented as [docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md](./docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md); Sprint-2.5 candidate
- ✅ U10 — [Sprint-2-redux sign-off](./docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md); CHANGELOG entry; README + CLAUDE current-stage update; tag `v0.4.0-sprint-2-redux`

**Next implementation step**: cut a fresh branch from `v0.4.0-sprint-2-redux` and start either:
- **Sprint-2.5** (the cross-module outbox table-name rename) — small focused unit to close the U9 gap, OR
- **Sprint-3-redux** (W5 Outbound + fulfillment saga) — original 12-week roadmap; can run in parallel with the Sprint-2.5 fix since Outbound doesn't depend on Inbound→Inventory's flow being physically-shared-DB-safe yet

**Sprint-2-redux deviations from plan file list**:
- **U6 — Domain event path swapped for explicit `IInboundOutbox`**: making `InboundConfirmedV1` implement `IDomainEvent` would create a SharedKernel → Contracts cycle. Pivoted to the explicit-outbox-write pattern matching Sprint-1-redux's `ReservationRepository.AppendOutbox`. `InboundLineConfirmedDomainEvent` deleted; Receiving aggregate no longer raises events.
- **U8 — MediatR command/handler wrapper deferred**: controllers call `ConfirmReceivingLineService` POCO directly. MediatR pipeline (logging/tracing/validation) is wired by `AddShopFlowDefaults` but no commands defined. Future sprint can layer command/handler on top trivially.
- **U8 — HTTP `WebApplicationFactory` tests skipped**: covered by U2/U3/U6 integration tests at the service + repo + consumer level.
- **U9 — Cross-module flow test deferred** (architecture finding above).
- **U4 — Identity-column annotation fix**: Npgsql's `IdentityByDefaultColumn` annotation needs the typed enum, not a plain string — discovered when zone insert tripped NOT NULL. Documented inline in the migration; carry-forward rule for future identity columns.

---

**Sprint-1-redux history** (kept for context; tag `v0.3.0-sprint-1-redux`). Sign-off: [`docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md`](./docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md). The reservation ledger ships against the DB-per-tenant foundation with the conditional-CTE INSERT at READ COMMITTED — the v3.0 correction over the v2.0 SERIALIZABLE shape.

**Sprint-1-redux progress** (as of 2026-05-12):
- ✅ U1 — `ReservationRepository.TryReserveAsync` — conditional-CTE INSERT at ReadCommitted + 23505 idempotency + StockReservedEvent outbox
- ✅ U2 — `FindByOrderIdAsync` + `ConfirmAsync` (NOT_FOUND/ALREADY_CONFIRMED/INVALID_STATE codes) + `ReleaseAsync` + `ReleaseExpiredAsync` multi-CTE batched UPDATE; Reservation + StockItem domain methods filled in
- ✅ U3 — Multiplexed `ReservationExpiryWorker` BackgroundService; `InventoryOptions` config surface (`ExpiryPollIntervalSeconds`, `ExpiryBatchSize`, `DefaultReservationTtlMinutes`)
- ✅ U4 — `ShopFlow.PropertyTests` project: `PostgresPropertyFixture` + `NotImplementedReservationRepository` adapter + 5 FsCheck properties (HappyPathConcurrency / StrictCapacity / Idempotency / ExpiryReleasesActiveRows / InvariantHoldsForAnyOperationSequence)
- ✅ U5 — `MultiTenantScaleGateTests` (5×1000 fairness floor gate + 1-stock-N-callers oversell variant) + `TenantHarness` + `FairnessCalculator`
- ✅ U6 — [Sprint-1-redux sign-off](./docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md); [docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md](./docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md); CHANGELOG entry; tag `v0.3.0-sprint-1-redux`

**Next implementation step**: cut a fresh branch from `v0.3.0-sprint-1-redux` and start Sprint-2-redux (Inbound module W4). Plan still to be written. Read-back surface for `IReservationRepository` (`GetActiveSumAsync` / `GetConfirmedSumAsync`) carries forward as a Sprint-2-redux side-quest because Inbound's GRN reconciliation needs the same shape; once landed, Property 5 in the FsCheck suite swaps its raw-SQL ledger read for the port call.

**Sprint-1-redux deviations from plan file list**:
- **U4 — Property "zero test-body edits" relaxed**: R3 expected the archived Sprint-1 property bodies to flip green with only fixture wiring changes. The archived bodies target the pre-redux port shape (`Result<Guid>`, `Guid orderId`, explicit `tenantId` parameter). U8 pivoted the port to `Result<Reservation>` / `string orderId` / no tenant parameter. The 5 properties are re-derived with the same names + same pinned seed against the new port shape; the call sites changed, the invariants did not.
- **U4 — Property 5 read-back surface gap remains open**: the canonical `GetActiveSumAsync` / `GetConfirmedSumAsync` read-back is not declared on `IReservationRepository`. Property 5 reads the ledger directly via raw SQL as a stop-gap. Sprint-2-redux closes when Inbound also needs the read-back surface.
- **U5 — Scale-gate runtime deferred**: code-complete, tagged `Category=Integration` + `Category=Load`. Wall-time measurement on this dev machine deferred because Docker Desktop is installed but the daemon is not running (same blocker as Phase-0-redux U10). CI captures the number once first nightly run completes.
- **U1+U2 — Direct repository wiring**: the repository takes `InventoryDbContext` by DI from the U8-shipped scoped registration rather than via `IDbContextFactory<InventoryDbContext>`. Functionally equivalent for the request-scoped path (the DbContext is built per scope using `IRequestContext.DbConnectionString`); ShopFlow0003 exempt because construction lives in a service registration lambda. Multiplexed worker (U3) uses `IServiceScopeFactory.CreateAsyncScope` + `RequestContext.Bind` to flow tenant context. Open-generic factory plumbing remains in `AddShopFlowDefaults` for any future per-message dispatcher path.

---

**Phase-0-redux history** (as of 2026-05-12 — kept for resume context):

**Phase-0-redux progress** (as of 2026-05-12 session 3):
- ✅ U1 — Canon verification + AGENTS.md numbering fix (commit `0111ee7`)
- ✅ U2 — Repo skeleton, sln, props, `global.json` pinning .NET 9 (commit `a26f507`)
- ✅ U3 — Channel test fixtures cherry-picked (commit `31c8a07`)
- ✅ U4 — SharedKernel + 4 analyzers (ShopFlow0001-0004) + 8 passing unit tests; build clean (commit `a9a8c62`)
- ✅ U5 — ControlPlane quartet (Domain/Application/Infrastructure/Migrations) + Tenant aggregate + initial catalog migration + 16 state-machine tests (commit `6307242`)
- ✅ U6 — `shopflow-migrate` CLI: provision/apply/archive/restore/status + 35 unit tests (commit `ee616df`)
- ✅ U7 — Aspire AppHost (Postgres + PgBouncer + Redis + RabbitMQ + observability) + chained provisioning of catalog/dev1/dev2 + production handoff via `infrastructure/docker-compose.yml`
- ✅ U8 — Inventory module (Domain entities + value objects + 4 domain events; 3 Application ports; Infrastructure DbContext + 4 entity configs + repo skeletons throwing NIE; ReservationExpiryWorker hosted-service stub; InitialInventorySchema migration with mandatory `[Migration]`+`[DbContext]` attributes; Api 501 skeleton) + 16 Domain unit tests (commit `c9f642d`)
- ✅ U9 — Module shape replicated (Inbound/Outbound/Channel quartets, Analytics triplet, Gateway YARP scaffold); per-module AGENTS.md deltas; 5 smoke test projects locking the shape in CI (commit `2a9cd41`)
- ✅ U10 — CI workflows (`.github/workflows/ci.yml` + `chaos-nightly.yml`); ShopFlow0001-0004 analyzers promoted Warning → Error; `MigrationSmokeTests` (2) + `CrossTenantRoutingTests` (5) against Testcontainers Postgres; `shopflow-gate phase-0-redux` operational CLI; [phase-0-redux sign-off](./docs/phase-gates/2026-05-12-phase-0-redux-signoff.md); README + CHANGELOG updates; tag `v0.2.0-phase-0-redux`

**Phase-0-redux is complete.** Tag: `v0.2.0-phase-0-redux`. Sign-off: [`docs/phase-gates/2026-05-12-phase-0-redux-signoff.md`](./docs/phase-gates/2026-05-12-phase-0-redux-signoff.md).

**U10 deviations from plan file list**:
- `shopflow-gate` shipped as a NEW project under `tools/shopflow-gate/` (the plan said "carries forward" but the v2.0 implementation wasn't on this branch — `task gate` referenced an absent csproj). Minimal CLI implements `gate phase-0-redux` with 4 checks (catalog reachable, catalog migrated, all tenants Ready, PgBouncer reachable). The richer in-cluster checks (provisioning latency p99, RabbitMQ live, observability stack live) are Phase-2 deliverables; the CLI shape is stable so adding a check is one method.
- `MigrationSmokeTests` parameterizes over the two known DbContexts directly (ControlPlane + Inventory) rather than reflection-discovering all DbContexts. The reflection version is a Sprint-1-redux improvement — cheap when a third DbContext lands; not load-bearing for U10's "guards the v2.0 silent-no-op defect" goal.
- `CrossTenantRoutingTests` exercises the middleware directly with a synthetic `DefaultHttpContext` and a `FakeTenantCatalog` rather than spinning a full `TestServer`. The contract under test is the slug→TenantInfo→DbConnectionString binding plus a real database read through that binding — sufficient to catch any wrong-DB routing bug at the level that matters. Full TestServer with the entire request pipeline is a Sprint-1-redux upgrade.
- Aspire cold-start + provisioning latency p99 measurements **deferred** in the sign-off — Docker daemon isn't running on this dev machine. Documented as a one-line table update in the sign-off doc once a Docker-enabled session lands.
- CSharpier formatting cleanup **deferred** to a follow-up commit — 23 files inherited from U4-U6 don't match CSharpier output (mostly LF/CRLF + line-fold disagreements). CI's `csharpier --check` step will block on first run; one cleanup commit fixes them.

**U9 deviations from plan file list**:
- Smoke tests for the 4 module shapes are `[Fact]`-level checks that the marker class exposes the expected `ModuleName` string. Gateway smoke test inspects the rendered `appsettings.json` for the 5 expected route names; this is a structural assertion, not a YARP integration test (the full integration suite lives in `tests/ShopFlow.<Module>.IntegrationTests/` per AGENTS.md §11.81 once real handlers land).
- Gateway routes use `http://<module>:8080` Docker-DNS-style destinations in `appsettings.json` — that's the W6 split-host shape. In the W1-W5 modular-monolith stance the modules all run as in-process controllers under one Aspire host, so the gateway routes are aspirational. U10 wires the AppHost to register each module's API as an Aspire resource so the gateway upstream addresses resolve.
- Per-module AGENTS.md files are ≤ 50 lines each per root rule 82; the U9 stub-state notes are intentionally terse so they're easy to delete when Phase-1+ replaces them.

**U8 deviations from plan file list**:
- `StockItem` inherits `BaseEntity` (not `AggregateRoot`) because the inherited `byte[] RowVersion` on `AggregateRoot` doesn't match the Postgres `xid` shape Tech Design v3.0 §4.2 wants; `StockItem` declares its own `uint RowVersion`. EF `Ignore`s the inherited Guid `Id`; `HasKey(s => s.Sku)` is the natural PK. The domain-event buffer from `BaseEntity` survives.
- A 4th test project landed (`tests/ShopFlow.Inventory.UnitTests/` — 16 tests for Sku/Quantity/Reservation.Create + Sprint-1-redux NIE detection). The plan didn't strictly require it, but the Domain primitives have real behavior that's cheap to lock down here; Sprint-1-redux behavior tests live in a separate Integration/Property project later.
- Inventory.Api `Program.cs` does NOT call `services.AddShopFlowDefaults(...)` yet — that composition entry point lands in U9-U10 along with the kernel-wide composition root. The Api project compiles and the controller returns 501 either way; the wiring gap is documented in the Program.cs comment and is a U10 concern.

**U7 deviations from plan file list**: AppHost csproj requires `<Sdk Name="Aspire.AppHost.Sdk" Version="13.3.0" />` (in addition to the `Aspire.Hosting.AppHost` package) — Aspire 13.x's MSBuild SDK is what tells .NET 9's `ImportWorkloads` target the workload is satisfied via NuGet; without it `dotnet build` raises NETSDK1147. CPM bumped `Microsoft.Extensions.Hosting` from 9.0.0 → 10.0.7 (Aspire 13.3.0 transitively requires the 10.x floor; cross-targets net9.0 cleanly). PgBouncer config is generated at AppHost startup from `infrastructure/pgbouncer/pgbouncer.ini.template` and bind-mounted into the bitnami container; the same template ships unmodified to the prod compose manifest. Mock channel servers are reserved as commented placeholders (Phase-2 Sprint-4 deliverable). The `task up` cold-start time + `GET /api/health` tenant-routing scenarios are deferred to U10 sign-off — Inventory.Api lands in U8.

**U5 deviation from plan file list**: `ITenantCatalog` port stays in `ShopFlow.SharedKernel.Application.Ports` (where U4 placed it) rather than being re-created in `ControlPlane.Application.Ports` — the SharedKernel routing middleware and outbox dispatcher consume the port and cannot take a backward dep on ControlPlane. `TenantStatus` enum relocated from `SharedKernel.Application.Ports` to `SharedKernel.Domain` (pure value type) so `ControlPlane.Domain.Tenant` and `Application.Ports.TenantInfo` share one enum without violating layering. The Migrations csproj omits `Microsoft.EntityFrameworkCore.Design` (NU1608 vs CPM-pinned CodeAnalysis 4.11.0); the runtime apply path used by `shopflow-migrate` and U10's smoke test does not need it.

**Session-1 decisions captured** (apply when resuming):
- **D1 PgBouncer pool sizing (U7)**: `pool_mode=transaction`, `default_pool_size=20`, `max_db_connections=20`, `min_pool_size=2`, `reserve_pool_size=5`. Postgres `max_connections=500` in dev, document `1000` for prod.
- **D2 Catalog cache (U5)**: 5 min TTL, LRU size 1000. Synchronous eviction on write paths (provision-complete, archive-start).
- **D3 Migration smoke test assertions (U8/U10)**: load-bearing assertion is `__ef_migrations_history` row count ≥ 1 after `MigrateAsync()`. Per-module named-table existence + named PK / UNIQUE constraint existence. No `pg_dump --schema-only` diff.
- **D4 Routing middleware (already implemented in U4)**: header > JWT > subdomain priority; 2+ source conflict → 403 + audit row; 10 concrete scenarios documented in `TenantRoutingMiddleware.cs`.

**Build/test invariants for resume**:
- `dotnet build` → 0 warnings, 0 errors across 41 projects (29 src + 11 test + 1 gate tool) — Inventory.IntegrationTests + PropertyTests added in Sprint-1-redux
- `dotnet test --filter "Category!=Integration"` → 92 passed (8 SharedKernel + 16 ControlPlane + 28 Inventory Domain + 35 Migrate + 5 module-shape smoke) — Sprint-1-redux added 12 Reservation/StockItem state-machine tests
- `dotnet test --filter "Category=Integration"` → ~26 tests (7 SharedKernel + 14 Inventory + 5 PropertyTests). Needs Docker; runs in CI on every PR.
- `dotnet test --filter "Category=Load"` → 2 tests in `MultiTenantScaleGateTests`. Needs Docker; nightly + on-demand only.
- .NET 9.0.305 SDK pinned via `global.json`; Aspire AppHost MSBuild SDK 13.3.0 referenced by `ShopFlow.AppHost.csproj`
- Pre-existing csharpier drift on 23 files (mix of LF/CRLF inheritance + line-fold disagreements) carried over from U4-U6 commits; Sprint-1-redux added a handful more files that may also drift. Husky pre-commit hook is not yet installed on this dev machine (`.husky/_/` absent), so commits do not enforce csharpier locally. CI's `csharpier --check` step will block on first run; one cleanup commit fixes them.

**Always read `docs/CHANGELOG.md` first** to understand what supersedes what. Then `docs/solutions/` for accumulated learnings (re-discovery prevention).

To resume implementation, the next concrete entry point is Sprint-2-redux (Inbound module W4) — plan still to be written. To deepen a design decision, run `/compound-engineering:ce-brainstorm` or `/compound-engineering:ce-plan`.
