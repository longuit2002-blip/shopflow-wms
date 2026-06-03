# ShopFlow WMS

> Multi-channel warehouse management system for SEA marketplaces, with database-per-tenant hard isolation under PDPA SEA compliance. 12-week single-developer portfolio build.

[![Proofs](https://img.shields.io/badge/hard--problem%20proofs-green%20locally-brightgreen)](#the-four-hard-problems-and-the-tests-that-prove-them)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#license)

**This is a portfolio build, and it's honest about what's worth your time.** The differentiator isn't WMS surface area — it's depth on four hard problems, each one provable by a test you can run on your own machine in a few minutes. Clone it, run `task proofs`, read the code the proofs exercise. The sprint-by-sprint history lives in [docs/sprint-history.md](docs/sprint-history.md); it's deliberately not the lead.

## The four hard problems (and the tests that prove them)

Each row links the production code to the test that proves the claim. All five proof suites run green locally via **`task proofs`** (Docker + Testcontainers required — no live cloud, no mocked-out invariants).

| Hard problem | Why it's hard | Code | Proving test |
|---|---|---|---|
| **Oversell-safe reservation ledger** | Concurrent flash-sale reservations against the same SKU must never oversell — without taking a row lock that collapses throughput. The solution is an append-only ledger with a conditional CTE INSERT at READ COMMITTED. | [ReservationRepository](src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Repositories/ReservationRepository.cs) | [MultiTenantScaleGateTests](tests/ShopFlow.Inventory.IntegrationTests/MultiTenantScaleGateTests.cs) (5 tenants × 1,000 concurrent → exactly 1,000 successes each, zero oversell) + [ReservationLedgerProperties](tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs) (FsCheck invariants) |
| **Noisy-neighbor multi-tenant sync** | One tenant bursting 2,000 stock changes/sec must not starve the others. A four-layer pipeline isolates per tenant: coalescing buffer → priority queue → token bucket → circuit breaker. | [StockSync engine](src/Services/StockSync/ShopFlow.StockSync.Infrastructure/) | [MultiTenantStockSyncScaleGateTests](tests/ShopFlow.StockSync.IntegrationTests/MultiTenantStockSyncScaleGateTests.cs) (tenant A floods; peers hold p99 SLO + fairness floor ≥ 0.85) |
| **Database-per-tenant isolation** | PDPA hard isolation: a request scoped to one tenant must read *only* that tenant's database, and a cross-tenant attempt must be rejected — never a silent leak. | [TenantRoutingMiddleware](src/Shared/ShopFlow.SharedKernel/Infrastructure/TenantRoutingMiddleware.cs) + per-tenant DbContext binding | [AuthCrossTenantTests](tests/ShopFlow.Auth.IntegrationTests/AuthCrossTenantTests.cs) + [CrossTenant403Test](tests/ShopFlow.Auth.IntegrationTests/Authorization/CrossTenant403Test.cs) + [CrossTenantRoutingTests](tests/ShopFlow.SharedKernel.IntegrationTests/CrossTenantRoutingTests.cs) |
| **Cross-role RBAC, defense-in-depth** | A Picker JWT must be denied a Dispatcher action — even against an order in the right pre-state — by a per-action policy that fires before the controller's state check. Four roles hand one order down a single saga. | [Outbound per-action policies](src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs) + `perm[]` JWT claims | [CrossRoleDenialTests](tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs) (14 denial facts) + [HandoffWorkflowTests](tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs) (Picker→Packer→Dispatcher hand-off) |

## Multi-channel is real, not asserted

A "multi-channel WMS" with one adapter is a hollow headline. ShopFlow ships **two** marketplace adapters — Shopee and Lazada — behind a [`ChannelAdapterFactory`](src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ChannelAdapterFactory.cs) that DI-enumerates by channel type, so a new channel is a pure registration with **zero framework edits**. The webhook receiver's signature extraction is channel-agnostic (each verifier declares its header). A single stock change fans out to **both** channels through the same noisy-neighbor pipeline:

- [MultiChannelSyncProofTests](tests/ShopFlow.StockSync.IntegrationTests/MultiChannelSyncProofTests.cs) — one stock change → a push to Shopee **and** Lazada, each logged.
- [LazadaWebhookReceiveTests](tests/ShopFlow.Channel.IntegrationTests/LazadaWebhookReceiveTests.cs) — a Lazada webhook is signature-verified, product-mapped, and persisted idempotently in the right tenant DB through the same pipeline Shopee uses.

## Run it

**Prerequisites:** Docker (for Testcontainers + the dev stack), .NET 9 SDK. If your machine already runs Postgres on `5432`, set `DevStack:PostgresHostPort` (e.g. `DevStack__PostgresHostPort=5433`) so the dev orchestrator's Postgres coexists; a clean machine with a free `5432` needs nothing.

```
task setup     # install dotnet tools (CSharpier, Husky.NET) + pre-commit hook
task proofs    # run the five hard-problem proof suites (Docker required) — the headline
task test      # full unit + integration + property suite (excludes Load)
task up        # Aspire dev orchestrator — infra + dashboard + tenant provisioning
```

`task proofs` is the fast path to "is this real?" — it boots Testcontainers Postgres per suite and runs the proofs above, independent of the full `task up` stack. `task up` boots the infrastructure, the Aspire dashboard, and provisions the control-plane catalog + two dev tenants; bringing every module API up behind the gateway is an ongoing dev-stack repair tracked in [the first-boot note](docs/solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md).

## What this is

A warehouse management system designed for SME registered businesses running 1-5K SKUs across 2-5 marketplaces with 100-1K orders/day. The thesis is **bounded sync latency with correctness guarantees at flash-sale load, delivered with database-per-tenant hard isolation that reads cleanly under PDPA audit**. Built at MVP scope (5 production-ready tenants, single Postgres cluster, mocked channel APIs) but designed so the path to **25-50 validated tenants under noisy-neighbor load** is concrete.

The full thesis with scale targets, SLOs, ADRs, and tier-by-tier rollout lives in two source-of-truth documents at the repo root: [`01-product-development-plan.md.docx`](01-product-development-plan.md.docx) (product) and [`02-technical-design-document.md.docx`](02-technical-design-document.md.docx) (architecture). The redesigned v3.0 markdown drafts are in [`docs/redesign/`](docs/redesign/); the .docx files are scheduled to be regenerated from those drafts.

## Architecture stance

Six bounded contexts (Inventory, Inbound, Outbound, Channel, Analytics, Gateway) plus Auth, StockSync, and Notification modules, bootstrapped as a **modular monolith** ([ADR-0002](docs/adr/0002-modular-monolith-first.md)) — one .NET solution, logical modules in separate `.csproj` per bounded context, single host, in-memory MediatR. Mechanical service split is a planned **W6 event** triggered by the channel adapter framework's arrival.

Multi-tenancy is **database-per-tenant on a shared Postgres cluster** ([ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md)). Each tenant maps to one logical Postgres DATABASE; routing happens in middleware (header → JWT claim → subdomain priority); PgBouncer in transaction-pooling mode is the connection multiplexer. A separate `shopflow_control` database holds the tenant catalog. Right-to-erasure is `DROP DATABASE` after retention window.

**Stack**: C# .NET 9 (canon declares .NET 8; the repo pins net9.0 via `global.json` because that is the SDK available on the developer machine — see [Directory.Build.props](Directory.Build.props)), Postgres 16, **PgBouncer**, Redis, RabbitMQ, MassTransit (sagas + outbox), OpenTelemetry, Aspire AppHost (dev) + hand-maintained Docker Compose (production handoff per [ADR-0001](docs/adr/0001-aspire-vs-docker-compose.md)).

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
│   ├── brainstorms/               requirements docs (incl. the portfolio finish-line)
│   ├── phase-gates/               per-sprint sign-offs
│   ├── redesign/                  v3.0 markdown drafts (to be regenerated to .docx)
│   ├── solutions/                 compounding learnings
│   ├── sprint-history.md          chronological release index
│   └── CHANGELOG.md               canon supersession history
├── src/
│   ├── ApiGateway/ShopFlow.Gateway/        YARP reverse proxy
│   ├── AppHost/ShopFlow.AppHost/           Aspire dev orchestrator
│   ├── ControlPlane/                       tenant catalog + provisioning catalog DB
│   ├── Services/{Inventory,Inbound,Outbound,Channel,Analytics,Auth,StockSync,Notification}/   bounded contexts
│   └── Shared/{SharedKernel,SharedKernel.Analyzers,Contracts}/    cross-cutting + Roslyn rules
├── tests/                                  unit + integration + property + proof projects
├── infrastructure/                         pgbouncer + docker-compose production handoff
└── tools/
    ├── extract-docs.{sh,ps1}               .docx text extraction
    ├── mocks/{shopee,lazada}/              marketplace mock servers
    ├── shopflow-migrate/                   per-tenant migration runner CLI
    └── shopflow-gate/                      phase-gate runner CLI
```

## Project status & history

- **Portfolio finish-line** (current): make the hard problems demonstrable + multi-channel honest — see the requirements + status in [docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md](docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md).
- **Release chronology**: [docs/sprint-history.md](docs/sprint-history.md) — every tag from Phase-0-redux through Sprint-13, one line + sign-off link each.
- **Supersession record**: [docs/CHANGELOG.md](docs/CHANGELOG.md) — what supersedes what.
- **The redesign decision**: [ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md) (database-per-tenant). Canon: [product plan v3.0](docs/redesign/01-product-development-plan.md), [tech design v3.0](docs/redesign/02-technical-design-document.md).

## Historical reference

Phase-0 (the v2.0 RLS-shared design) and Phase-1 Sprint-1 work-in-progress live at:
- Branch: `archive/phase-1-sprint-1-rls-shared`
- Tag: `archive/v0.1.0-phase-0-rls-shared` (annotated supersession note)

Both remain in the remote forever as the "what we redesigned away from" reference.

## License

TBD.
