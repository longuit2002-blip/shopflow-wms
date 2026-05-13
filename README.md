# ShopFlow WMS

> Multi-channel warehouse management system for SEA marketplaces, with database-per-tenant hard isolation under PDPA SEA compliance. 12-week single-developer portfolio build.

[![Stage](https://img.shields.io/badge/stage-Sprint--2.5%20%E2%9C%85-brightgreen)](docs/phase-gates/2026-05-13-sprint-2.5-signoff.md)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#license)

**Current stage**: **Phase-1 Sprint-2.5 complete (2026-05-13)** — tagged `v0.4.1-sprint-2.5`. Sprint-2.5 closes the Sprint-2-redux U9 deferral: per-module outbox table-name prefix (`inbound_outbox_messages` / `inventory_outbox_messages`) unblocks single-physical-tenant-DB cross-module flow. Two `InboundToInventoryFlowTests` validate the full Inbound → outbox → MassTransit publish → InboundConfirmedConsumer → Inventory stock pipeline against a shared Testcontainers Postgres. While writing the test, a latent JSON-options bug surfaced (camelCase serialise vs case-sensitive deserialise dropped all payload properties to defaults — Sprint-1-redux ship that no consumer had yet exercised) — fixed by centralising `ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default` across 4 call sites. 110 unit + 54 integration tests green. See [Sprint-2.5 sign-off](docs/phase-gates/2026-05-13-sprint-2.5-signoff.md). **Previous**: [Sprint-2-redux sign-off](docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md) (`v0.4.0-sprint-2-redux`). **Next**: Sprint-3-redux (Outbound + fulfillment saga, W5).

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
