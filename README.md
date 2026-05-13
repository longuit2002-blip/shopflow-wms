# ShopFlow WMS

> Multi-channel warehouse management system for SEA marketplaces, with database-per-tenant hard isolation under PDPA SEA compliance. 12-week single-developer portfolio build.

[![Stage](https://img.shields.io/badge/stage-Sprint--4%20%E2%9C%85-brightgreen)](docs/phase-gates/2026-05-13-sprint-4-signoff.md)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#license)

**Current stage**: **Phase-2 Sprint-4 complete (2026-05-13)** — tagged `v0.6.0-sprint-4`. Opens Phase-2's channel-ingress half. The Channel module ships: 3 Domain aggregates (Channel, WebhookEvent, ProductMapping) + value objects, full `ChannelDbContext` + `InitialChannelSchema` migration (4 tables with `UNIQUE(channel_id, provider_event_id)` as the webhook idempotency anchor), webhook receiver pipeline (`ShopeeSignatureVerifier` HMAC-SHA256 + `FixedTimeEquals` constant-time compare, `WebhookEventRepository` UNIQUE-23505 catch mirroring Sprint-1-redux's pattern, `IngestWebhookService` orchestrator with first-write-only outbox append, `[SkipTenantRouting]` middleware opt-out, `WebhooksController` 404/401/200 surface), **K13 close** (`IOutboxRouteRegistry` + `OutboxRoute` + `SendKind` + `services.AddOutboxRoute<T>(...)` extension; `MultiplexedOutboxDispatcher` branches Send vs Publish per row; unregistered types route to `OutboxRoute.PublishDefault` so Sprint-1/2/3 paths are unchanged — Phase-2 W6 mechanical-split prerequisite shipped), `IChannelAdapter` framework + Shopee adapter + parser (Lazada is one DI line in Sprint-6), three-tier product mapping engine (Exact → Levenshtein @ threshold 0.6 → null), separate-process Shopee mock server at `tools/mocks/shopee/` (Channel AGENTS.md §11.6 discipline) wired into Aspire as `AddProject<>`, `OrderImportedV1` cross-module contract + `OrderImportedConsumer` in Outbound (idempotent on `Order.ChannelExternalOrderId` UNIQUE, reuses Sprint-3 ports — no self-HTTP loopback), Channel.Api Program.cs fully composed. 269 unit tests green (+30 from Sprint-4). `ShopFlow.Channel.IntegrationTests` project skeleton + `MultiTenantWebhookScaleGateTests` declared with 3 `Skip`'d Category=Load slots (harness body deferred to follow-up). See [Sprint-4 sign-off](docs/phase-gates/2026-05-13-sprint-4-signoff.md). **Previous**: [Sprint-3-redux sign-off](docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md) (`v0.5.0-sprint-3-redux`). **Next**: Sprint-5 (Stock Sync Engine — coalescing buffer + per-channel token bucket + priority queue + circuit breaker + allocation engine; noisy-neighbor scale gate). Optional Sprint-4.5 follow-up commit lands the parser wire-up + harness body before opening Sprint-5.

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
