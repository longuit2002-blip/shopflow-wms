# ShopFlow WMS

> Multi-channel warehouse management system for SEA marketplaces, with database-per-tenant hard isolation under PDPA SEA compliance. 12-week single-developer portfolio build.

[![Stage](https://img.shields.io/badge/stage-redesign%20%E2%86%92%20Phase--0--redux-orange)](docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#license)

**Current stage**: **Multi-tenancy redesign accepted (2026-05-11)** — Phase-0 (RLS-shared) and Phase-1 Sprint-1 work-in-progress are archived. The system pivots to **database-per-tenant on shared Postgres cluster** under [ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md) for PDPA SEA hard isolation. New canon ([product plan v3.0](docs/redesign/01-product-development-plan.md), [tech design v3.0](docs/redesign/02-technical-design-document.md)), implementation plans ([Phase-0-redux](docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md), [Sprint-1-redux](docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md)), and AGENTS.md §3 rewrite are committed to `main`.

The Phase-0-redux implementation runs on branch `feat/phase-0-redux-db-per-tenant`. The historical Phase-0 + Sprint-1 work is preserved at branch `archive/phase-1-sprint-1-rls-shared` and tag `archive/v0.1.0-phase-0-rls-shared`.

See [docs/CHANGELOG.md](docs/CHANGELOG.md) for the supersession record.

## What this is

A warehouse management system designed for SME registered businesses running 1-5K SKUs across 2-5 marketplaces with 100-1K orders/day. The thesis is **bounded sync latency with correctness guarantees at flash-sale load, delivered with database-per-tenant hard isolation that reads cleanly under PDPA audit**. Built at MVP scope (5 production-ready tenants, single Postgres cluster, mocked channel APIs) but designed so the path to **25-50 validated tenants under noisy-neighbor load** is concrete.

The full thesis with scale targets, SLOs, ADRs, and tier-by-tier rollout lives in two source-of-truth documents at the repo root: [`01-product-development-plan.md.docx`](01-product-development-plan.md.docx) (product) and [`02-technical-design-document.md.docx`](02-technical-design-document.md.docx) (architecture). The redesigned v3.0 markdown drafts are in [`docs/redesign/`](docs/redesign/); the .docx files are scheduled to be regenerated from those drafts.

## Architecture stance

Six bounded contexts (Inventory, Inbound, Outbound, Channel, Analytics, Gateway), bootstrapped as a **modular monolith** ([ADR-0002](docs/adr/0002-modular-monolith-first.md)) — one .NET solution, six logical modules in separate `.csproj` per bounded context, single host, in-memory MediatR. Mechanical 6-service split is a planned **W6 event** triggered by the channel adapter framework's arrival.

Multi-tenancy is **database-per-tenant on a shared Postgres cluster** ([ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md)). Each tenant maps to one logical Postgres DATABASE; routing happens in middleware (header → JWT claim → subdomain priority); PgBouncer in transaction-pooling mode is the connection multiplexer. A separate `shopflow_control` database holds the tenant catalog. Right-to-erasure is `DROP DATABASE` after retention window.

**Stack**: C# .NET 8, Postgres 16, **PgBouncer**, Redis, RabbitMQ, MassTransit (sagas + outbox), OpenTelemetry, Aspire AppHost (dev) + hand-maintained Docker Compose (production handoff per [ADR-0001](docs/adr/0001-aspire-vs-docker-compose.md)).

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
└── tools/
    ├── extract-docs.{sh,ps1}      .docx text extraction
    └── (shopflow-gate, shopflow-migrate land in Phase-0-redux)
```

## Getting started

This repo is in transition. To work on the redesign implementation:

1. Read [docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md](docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md) — the plan-of-plans that drives everything.
2. Read [docs/redesign/02-technical-design-document.md](docs/redesign/02-technical-design-document.md) §1 (multi-tenancy) and §2 (provisioning) — the architectural foundation.
3. Read [ADR-0003](docs/adr/0003-database-per-tenant-for-compliance.md) — the supersession decision.
4. Check out the implementation branch:
   ```
   git checkout feat/phase-0-redux-db-per-tenant
   ```
5. Follow [docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md](docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md) U1 → U10.

## Historical reference

Phase-0 (the v2.0 RLS-shared design) and Phase-1 Sprint-1 work-in-progress live at:
- Branch: `archive/phase-1-sprint-1-rls-shared`
- Tag: `archive/v0.1.0-phase-0-rls-shared` (annotated supersession note)

Both remain in the remote forever as the "what we redesigned away from" reference. Three `docs/solutions/` learnings carry forward: EF migration attributes, FsCheck Replay format, green-against-stub property pattern.

## License

TBD.
