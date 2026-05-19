# ShopFlow WMS

> Multi-channel warehouse management system for SEA marketplaces, with database-per-tenant hard isolation under PDPA SEA compliance. 12-week single-developer portfolio build.

[![Stage](https://img.shields.io/badge/stage-Sprint--7%20Orders%20Saga%20%E2%9C%85-brightgreen)](docs/phase-gates/2026-05-19-sprint-7-signoff.md)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#license)

**Current stage**: **Sprint-7 Orders Saga Visualisation complete (2026-05-19)** — tagged `v0.10.0-sprint-7-orders`. Second frontend vertical slice ships at `/orders` + `/orders/$orderId`: horizontal `<SagaPipeline>` watching the 11-state fulfillment saga (Placed → Reserved → AwaitingPick → … → Shipped) with sub-second freshness via SignalR push; `<TransitionsLog>` newest-first feed with `aria-live="polite"`; `<OrderLineItems>` (KTD11 cell-level button) reuses Sprint-6's `<LedgerDrawer>` via a `SkuListItem` stub adapter. **Closes Sprint-6 trade-off #9** by lifting SignalR into shared infrastructure under a **single hub-host topology** (only `Outbound.Api` maps `/hub` + registers the two relay consumers `StockChangedRelayConsumer` + `SagaTransitionedRelayConsumer`; Gateway routes `/hub` there permanently — avoids the RabbitMQ competing-consumer trap on the eventual W6 split). New `outbound_saga_transitions` per-tenant audit table written by `SagaTransitionObserver` via explicit per-branch `.ThenAsync` hooks at every `TransitionTo` site in `FulfillmentSaga` (9 transitions instrumented incl. `WhenEnter` IfElse Path A + `If` counter-drain + compound `Reserved → AwaitingPick` chain). JwtBearer lifted into `AddShopFlowDefaults` (closes Sprint-6 trade-off #8 in spirit; real auth still Sprint-8) + access-token query-parameter redaction in `JwtBearerEvents.OnMessageReceived` so `?access_token=` never leaks to request logs. Dev-mode `POST /api/outbound/orders/seed` returns 404 + `environment_not_dev` outside Development. Frontend: ~351 Vitest tests (+130 from Sprint-6); 10 axe-smoke assertions; `@microsoft/signalr ^8.0.7`; `useInventoryQuery` + `useOrdersQuery` toggle `refetchInterval` based on hub state (R13 polling fallback intact). **Doc-review pipeline executed** before start: 5 P1 findings + 2 architectural premise challenges walked through; 1 `safe_auto` fix applied (Auth.Api dropped from hub mapping); 3 P1s declared resolved + 2 routed via user-decision (single-hub-host + IStateObserver-via-explicit-wiring) before U1. **Subagent dispatch mode shipped**: 14 units across 5 rounds (2 inline + 3 parallel — 5+3+2 subagents); orchestrator reviewed each diff + committed serially. See [Sprint-7 sign-off](docs/phase-gates/2026-05-19-sprint-7-signoff.md). **Previous**: [Sprint-6 sign-off](docs/phase-gates/2026-05-19-sprint-6-signoff.md) (`v0.9.0-frontend-vertical-slice`). **Next**: Sprint-7.5 (bundled Sprint-6 trade-off closures — cosmetic SKU schema + camelCase wire + flash-sale dual-write + URL-search-params), Sprint-8 (real auth + first multi-role surface), Sprint-5.5 (parallel-track scale-gate harness), or Phase-3 polish.

**Sprint-6 Frontend Vertical Slice complete (2026-05-19)** — tagged `v0.9.0-frontend-vertical-slice`. First frontend surface ships in a new top-level `web/` subdirectory: Vite 5 + React 19 + TypeScript strict + TanStack Router (file-based) + TanStack Query (2-s polling) + Zustand. Inventory screen × Owner role end-to-end through real `Inventory.Api` WRITE controllers (`POST /adjustments`, `PUT /skus/{sku}/threshold`, `PUT /skus/{sku}/flash-sale`, `POST /skus`), with `Adjust Stock` modal + inline threshold edit (optimistic UI via React 19 set-state-during-render) + `Flash-sale` toggle + `Create SKU` modal. 8 other screens ship as `ComingSoon` placeholders behind the auth guard. Stub `Auth.Api` returns a baked JWT (Sprint-7 → real auth). Frontend CI job runs typecheck + lint + 221 Vitest tests (incl. 6 axe-clean a11y smoke assertions) + build in parallel with .NET jobs. **KTD11** (emergent in U13): `nested-interactive` axe violation caught by the new harness — SkuTable refactored so the row drops button semantics + first-column SKU cell hosts the button. See [Sprint-6 sign-off](docs/phase-gates/2026-05-19-sprint-6-signoff.md). **Previous**: [Methodology Writeup sign-off](docs/phase-gates/2026-05-18-methodology-writeup-signoff.md) (`v0.8.0-methodology-writeup`).

**Phase-3 Methodology Writeup complete (2026-05-18)** — tagged `v0.8.0-methodology-writeup`. Ships [docs/methodology.md](docs/methodology.md) (~8500 words) — a comprehensive 7-sprint chronological case study + synthesis pattern catalog + friction section documenting the AI-assisted development methodology used to build ShopFlow WMS. Audience: future-self + developers cloning the repo (not HR/recruiter). Output: 100% in `docs/`; no code changes. Sign-off: [docs/phase-gates/2026-05-18-methodology-writeup-signoff.md](docs/phase-gates/2026-05-18-methodology-writeup-signoff.md). **Previous**: [Sprint-5 sign-off](docs/phase-gates/2026-05-17-sprint-5-signoff.md) (`v0.7.0-sprint-5`). **Next**: Sprint-5.5 (close scale-gate harness — see methodology friction mode 4), Sprint-6 (Analytics — W9-W10), or public blog derivative.

**Phase-2 Sprint-5 complete (2026-05-17)** — tagged `v0.7.0-sprint-5`. Closes Phase-2 egress with a new `ShopFlow.StockSync` module (7th logical module in the modular monolith) consuming Inventory's stock-mutation outbox stream and pushing per-channel `available_to_sell` updates through a four-layer isolation pipeline: coalescing buffer per `(tenant, sku, channel)` → per-tenant priority queue (high/normal lanes for flash-sale routing) → token bucket per `(tenant, channel)` → Polly v8 circuit breaker → `IChannelAdapter.PushStockUpdateAsync` → push-log audit row. **KTD1** replaces the literal brainstorm R1 (consume 3 transition events) with a single canonical `StockLevelChangedV1` event emitted from Inventory's 5 stock-mutating repository paths + the put-away `AdjustAtBin` path — clean cross-module shape, no Outbound coupling. `ShopeeAdapter.PushStockUpdateAsync` body fills the Sprint-4 stub with real HTTP POST + status-code → stable error-code mapping; Shopee mock gains `/api/v2/product/update_stock` + chaos toggle. SkuFlag admin API + caching wrapper (5min TTL, opens DI scope + binds tenant via K12 pattern from singleton context). 359 unit tests green (+71 from Sprint-5). +24 `Category=Integration` tests. +2 `Category=Load` scale-gate slots Skip'd per Sprint-4 U9 precedent — production primitives all proven by U3-U8 unit + integration coverage; wall-time noisy-neighbor measurement deferred to Sprint-5.5 follow-up (multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api). See [Sprint-5 sign-off](docs/phase-gates/2026-05-17-sprint-5-signoff.md). **Previous**: [Sprint-4.5 sign-off](docs/phase-gates/2026-05-15-sprint-4.5-signoff.md) (`v0.6.1-sprint-4.5`). **Next**: Sprint-5.5 (close scale-gate harness), Sprint-6 (Analytics module W9-W10 — read-side projections), or Phase-3 polish (Gateway hardening, observability, portfolio README + demo).

**Phase-2 Sprint-4.5 complete (2026-05-15)** — tagged `v0.6.1-sprint-4.5`. Closes the four Sprint-4 sign-off deferrals as a ~1-week point release: receiver `provider_event_id` now sourced from the marketplace-asserted Shopee `event_id` via `IChannelAdapterFactory.ResolveFor(channelType).ParseWebhook(...)` (body-hash stub deleted); `WebhookOrchestrator` gates on `event_type == "order.created"`, resolves per-line external→internal SKUs via `IProductMappingService`, and emits the canonical `OrderImportedV1` (or **fails the whole import** per contract canon when any line is unmapped — brainstorm R6 reversal documented); `TenantWebhookHarness` integration helper with `WebApplicationFactory<Program>`-backed Channel.Api host + multi-tenant Postgres provisioning; three `Category=Load` scale-gate bodies in place (burst-200rps × 5 tenants + replay-100× idempotency + cross-tenant signature → 401). 288 unit tests green (+19 from Sprint-4.5: 13 ShopeeAdapterParseOrderCreated + 7 WebhookOrchestrator). 1 new `Category=Integration` smoke test; 3 `Category=Load` tests runnable in CI. See [Sprint-4.5 sign-off](docs/phase-gates/2026-05-15-sprint-4.5-signoff.md). **Previous**: [Sprint-4 sign-off](docs/phase-gates/2026-05-13-sprint-4-signoff.md) (`v0.6.0-sprint-4`). **Next**: Sprint-5 Stock Sync Engine (Phase-2 W6-W8 centerpiece — coalescing buffer + per-channel token bucket + priority queue + circuit breaker + allocation engine; noisy-neighbor scale gate).

**Phase-1 Sprint-3-redux complete (2026-05-13)** — tagged `v0.5.0-sprint-3-redux`. **Phase-1 customer funnel closed**: Inventory holds stock (Sprint-1-redux), Inbound fills it (Sprint-2-redux), Outbound drains it (Sprint-3-redux). The Outbound module ships the full fulfillment saga (MassTransit state machine, 11 states, EF saga repository with K12 per-tenant DbContext binding via `TenantBindingSagaFilter`), 9 cross-module contracts, 3 new Inventory consumers wrapping the extended `ReservationRepository` (`TryReserveLinesAsync` + `ReleaseLinesAsync` — atomic multi-line CTE; `reservations_ledger` schema gained `order_line_id` with composite UNIQUE), `IPickQueue` per-tenant `Channel<PickRequestV1>` + `PickWaveGeneratorService` (15-min window batching, round-robin picker), mocked shipping carrier with Polly v8 retry, pick-failure compensation via Set-based release dedup, and `OrderCancelledConsumer` propagating saga terminal state to the Order row. K15 verified: `MassTransit.EntityFrameworkCore` 8.3.4 + EF Core 9 bind cleanly. K11 multi-row CTE concurrency fix landed as institutional learning ([docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md](docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md)) — pre-check CTEs are unsafe under READ COMMITTED; predicate must live inside the UPDATE. W5 scale gate (`Category=Load`): 2000 orders × 3 tenants, dev-laptop Shipped p99 247-332ms/tenant + Cancelled p99 112-131ms/tenant + fairness floor 0.918-0.979 (operator-pipeline path; saga bypassed — documented limitation; production CI re-validates). ~270 unit + ~120 integration + 4 load tests green. See [Sprint-3-redux sign-off](docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md). **Previous**: [Sprint-2.5 sign-off](docs/phase-gates/2026-05-13-sprint-2.5-signoff.md) (`v0.4.1-sprint-2.5`). **Next**: Phase-2 Sprint-4 (Channel Connections + webhook idempotency) cuts from `v0.5.0-sprint-3-redux`.

**Phase-1 Sprint-2-redux complete (2026-05-13)** — tagged `v0.4.0-sprint-2-redux`. The Inbound module ships: PurchaseOrder + Receiving aggregates with auto-state-transition state machine, per-line confirmation via `ConfirmReceivingLineService` (writes the ledger + outbox row atomically), append-only `reconciliation_tickets` log on quantity mismatch, thin HTTP controllers. Inventory schema gains zones / bins / stock_item_bins / inbound_dedup tables + nullable `home_zone_id` FK on stock_items. New cross-module flow: `ShopFlow.Contracts.Inbound.InboundConfirmedV1` event flows through the outbox dispatcher to `InboundConfirmedConsumer` in Inventory, which auto-creates stock_items, applies bin-targeted stock change via the new `AdjustAtBinAsync` (UPSERT stock_items + UPSERT stock_item_bins + UPDATE bins.occupancy_qty + INSERT stock_adjustments — all in one ReadCommitted transaction), and dedups against the `inbound_dedup(receiving_id, line_id)` table. MassTransit transport flipped from in-memory to real RabbitMQ via a config knob (`ShopFlowDefaultsOptions.MessageBusTransport`) — promoted from W6 to W4 so Sprint-3-redux's saga inherits production-shape broker semantics. 110 unit tests + 52 integration tests green. Architecture finding [docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md](docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md) captures U9's deferred cross-module flow test (Sprint-2.5 candidate). See [Sprint-2-redux sign-off](docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md) for measured numbers + deviations. **Previous**: [Sprint-1-redux sign-off](docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md) (`v0.3.0-sprint-1-redux`). **Next**: Sprint-2.5 (outbox table-name rename) or Sprint-3-redux (Outbound + saga) — plans TBD.

Multi-tenancy redesign captured under [ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md): **database-per-tenant on shared Postgres cluster** for PDPA SEA hard isolation. Canon: [product plan v3.0](docs/redesign/01-product-development-plan.md), [tech design v3.0](docs/redesign/02-technical-design-document.md). The historical Phase-0 + Sprint-1 work is preserved at branch `archive/phase-1-sprint-1-rls-shared` and tag `archive/v0.1.0-phase-0-rls-shared`.

See [docs/CHANGELOG.md](docs/CHANGELOG.md) for the supersession record.

## What this is

A warehouse management system designed for SME registered businesses running 1-5K SKUs across 2-5 marketplaces with 100-1K orders/day. The thesis is **bounded sync latency with correctness guarantees at flash-sale load, delivered with database-per-tenant hard isolation that reads cleanly under PDPA audit**. Built at MVP scope (5 production-ready tenants, single Postgres cluster, mocked channel APIs) but designed so the path to **25-50 validated tenants under noisy-neighbor load** is concrete.

The full thesis with scale targets, SLOs, ADRs, and tier-by-tier rollout lives in two source-of-truth documents at the repo root: [`01-product-development-plan.md.docx`](01-product-development-plan.md.docx) (product) and [`02-technical-design-document.md.docx`](02-technical-design-document.md.docx) (architecture). The redesigned v3.0 markdown drafts are in [`docs/redesign/`](docs/redesign/); the .docx files are scheduled to be regenerated from those drafts.

## Architecture stance

Six bounded contexts (Inventory, Inbound, Outbound, Channel, Analytics, Gateway), bootstrapped as a **modular monolith** ([ADR-0002](docs/adr/0002-modular-monolith-first.md)) — one .NET solution, six logical modules in separate `.csproj` per bounded context, single host, in-memory MediatR. Mechanical 6-service split is a planned **W6 event** triggered by the channel adapter framework's arrival.

Multi-tenancy is **database-per-tenant on a shared Postgres cluster** ([ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md)). Each tenant maps to one logical Postgres DATABASE; routing happens in middleware (header → JWT claim → subdomain priority); PgBouncer in transaction-pooling mode is the connection multiplexer. A separate `shopflow_control` database holds the tenant catalog. Right-to-erasure is `DROP DATABASE` after retention window.

**Stack**: C# .NET 9 (canon declares .NET 8; Phase-0-redux pins net9.0 via `global.json` because that is the SDK available on the developer machine — see [Directory.Build.props](Directory.Build.props)), Postgres 16, **PgBouncer**, Redis, RabbitMQ, MassTransit (sagas + outbox), OpenTelemetry, Aspire AppHost (dev) + hand-maintained Docker Compose (production handoff per [ADR-0001](docs/adr/0001-aspire-vs-docker-compose.md)).

## Compliance posture

PDPA Vietnam (Decree 13/2023/ND-CP) + Singapore PDPA. Hard isolation answers "how do you guarantee data segregation?" with two databases, two backups, two `DROP DATABASE` blast radii. SOC2 / ISO 27001 are explicit non-goals at this stage; the architecture supports them, the controls work is operational follow-up. See [product plan v3.0 §4](docs/redesign/01-product-development-plan.md#4-compliance-scope).

## Repo layout

```
.
├── AGENTS.md                      AI-pair-programming rule canon (auto-loaded by Claude/Cursor/Copilot)
├── CLAUDE.md                      project context for AI assistants
├── README.md                      this file
├── 01-product-development-plan.md.docx    canonical product spec (v3.0 draft in docs/redesign/)
├── 02-technical-design-document.md.docx   canonical tech design (v3.0 draft in docs/redesign/)
├── docs/
│   ├── adr/                       numbered architectural decisions (immutable; postscripts allowed)
│   ├── plans/                     active and superseded work plans
│   ├── redesign/                  v3.0 markdown drafts (to be regenerated to .docx)
│   ├── solutions/                 compounding learnings
│   ├── source/                    .docx → .txt extracts (gitignored)
│   ├── ideation/                  ranked candidate ideas
│   └── CHANGELOG.md               canon supersession history
├── src/
│   ├── ApiGateway/ShopFlow.Gateway/        YARP reverse proxy
│   ├── AppHost/ShopFlow.AppHost/           Aspire dev orchestrator
│   ├── ControlPlane/                       tenant catalog + provisioning catalog DB
│   ├── Services/{Inventory,Inbound,Outbound,Channel,Analytics}/   bounded contexts
│   └── Shared/{SharedKernel,SharedKernel.Analyzers,Contracts}/    cross-cutting + Roslyn rules
├── tests/                                  unit + integration test projects
├── infrastructure/                         pgbouncer + docker-compose production handoff
└── tools/
    ├── extract-docs.{sh,ps1}               .docx text extraction
    ├── shopflow-migrate/                   per-tenant migration runner CLI
    └── shopflow-gate/                      phase-gate runner CLI
```

## Getting started

```
task setup        # install dotnet tools (CSharpier, Husky.NET) + pre-commit hook
dotnet build      # 0 warnings / 0 errors expected
dotnet test --filter "Category!=Integration&Category!=Load"   # 80 unit tests pass
task up           # Aspire dev orchestrator — needs Docker
```

Reading order for new contributors:

1. [Sprint-1-redux reservation ledger plan](docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) — next implementation slice.
2. [Phase-0-redux sign-off](docs/phase-gates/2026-05-12-phase-0-redux-signoff.md) — what landed, what's deferred.
3. [Tech design v3.0](docs/redesign/02-technical-design-document.md) §1 (multi-tenancy), §2 (provisioning), §4 (reservation ledger).
4. [ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md) — the supersession decision.
5. [AGENTS.md](AGENTS.md) — the executable rule canon.

## Historical reference

Phase-0 (the v2.0 RLS-shared design) and Phase-1 Sprint-1 work-in-progress live at:
- Branch: `archive/phase-1-sprint-1-rls-shared`
- Tag: `archive/v0.1.0-phase-0-rls-shared` (annotated supersession note)

Both remain in the remote forever as the "what we redesigned away from" reference. Three `docs/solutions/` learnings carry forward: EF migration attributes, FsCheck Replay format, green-against-stub property pattern.

## License

TBD.
