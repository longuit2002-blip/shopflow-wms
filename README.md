# ShopFlow WMS

> Multi-channel inventory and fulfillment control plane for SEA marketplace sellers (Shopee, Lazada, TikTok Shop, Shopify). 12-week single-developer portfolio build.

[![Stage](https://img.shields.io/badge/stage-Phase--0%20bootstrap-blue)](docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#license)

**Current stage**: **Phase-0 complete** — tagged [`v0.1.0-phase-0`](docs/phase-gates/2026-05-10-phase-0-signoff.md). 30 .NET projects, 67 unit + property tests passing, 10 compounding learnings captured under `docs/solutions/`. Phase 1+ (Inventory ledger implementation, Inbound, Outbound saga, Multi-channel sync) lands in subsequent plans.

---

## What this is

A warehouse management system designed for SME sellers running 1-5K SKUs across 2-5 marketplaces with 100-1K orders/day. The thesis is **bounded sync latency with correctness guarantees at flash-sale load** — explicit oversell prevention via an append-only reservation ledger, per-channel rate-limited stock sync with coalescing and priority queueing, and persistent webhook idempotency. Built at MVP scope (single-tenant, Docker Compose, mocked channel APIs) but designed so the path to 10K paying sellers is obvious and the compromises are explicit.

The full thesis with scale targets, SLOs, ADRs, and tier-by-tier rollout lives in two source-of-truth documents at the repo root: [`01-product-development-plan.md.docx`](01-product-development-plan.md.docx) (product) and [`02-technical-design-document.md.docx`](02-technical-design-document.md.docx) (architecture).

## Architecture stance

Six bounded contexts (Inventory, Inbound, Outbound, Channel, Analytics, Gateway), but bootstrapped as a **modular monolith** — one .NET solution, six logical modules in separate `.csproj` per bounded context, single host, in-memory MediatR. Mechanical 6-service split is a planned **W6 event** triggered by the channel adapter framework's arrival, with its own scale gate. See [`docs/adr/0002-modular-monolith-first.md`](docs/adr/0002-modular-monolith-first.md) for the rationale.

```text
┌──────────── Web (Next.js 14, lands in Phase 1+) ────────────┐
│                            │                                │
│               ┌────────────▼─────────┐                       │
│               │  Gateway (YARP)      │                       │
│               └────────────┬─────────┘                       │
│                            │                                │
│   ┌──────────┬──────────┬──┴──────┬──────────┬───────────┐   │
│   │ Inventory│ Inbound  │ Outbound│ Channel  │ Analytics │   │
│   │  (★ ref) │          │  + Saga │ + Sync   │  (read)   │   │
│   └──────────┴──────────┴─────────┴──────────┴───────────┘   │
│              │ in-memory bus (W1-W5) → RabbitMQ (W6 split)   │
│   ┌──────────▼──────────────────────────────────────────┐    │
│   │   Postgres (RLS) · Redis · Outbox · OTel/Tempo      │    │
│   └─────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

**Stack**: C# .NET 8, Next.js 14, Postgres 16 (RLS + monthly partitioning), Redis, RabbitMQ, MassTransit (sagas + outbox), OpenTelemetry, Aspire AppHost (dev-only) + hand-maintained Docker Compose (production handoff per [`docs/adr/0001-aspire-vs-docker-compose.md`](docs/adr/0001-aspire-vs-docker-compose.md)).

## Quickstart

After cloning, the only documented setup command is:

```bash
task setup
```

This runs `dotnet tool restore` (installs CSharpier and Husky.NET locally) and `dotnet husky install` (wires the pre-commit hook). Idempotent — safe to re-run.

Then:

```bash
task --list-all   # see every documented task
task up           # start the dev orchestrator (lands in U9)
task test         # run unit + integration + property tests
task ci           # run the full CI sequence locally
```

The `.docx` source documents are committed; their plain-text equivalents are gitignored. Regenerate them on a new machine with:

```bash
task extract-docs
```

## Document map

| Document | Audience | What it carries |
|---|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Humans + AI helpers | Project context, working preferences, source-doc pointers |
| [`AGENTS.md`](AGENTS.md) | AI-pair-programming agents | Executable rule canon (~72 instructions, 200-cap budget) |
| [`docs/adr/`](docs/adr/) | Senior engineers reviewing | Numbered architectural decisions with When-this-breaks fields |
| [`docs/ideation/`](docs/ideation/) | Reviewers + future-self | Ranked idea candidates that informed planning |
| [`docs/plans/`](docs/plans/) | Implementers (human + agent) | Active work plans with U-IDs and scale-gate criteria |
| [`docs/source/`](docs/source/) (gitignored) | grep / search | Plain-text extracts of the .docx source-of-truth docs |
| [`tests/fixtures/channels/`](tests/fixtures/channels/) | Mock-channel server + integration tests | Synthetic-but-realistic Shopee + Lazada wire-shape payloads |

## Engineering anchors

These are the parts the design earns its keep on; reviewers should focus here:

- **Append-only reservation ledger** — CTE-based conditional INSERT, not row-locks. Solves the hot-key flash-sale contention problem at the SQL layer instead of the application layer. ([Tech Design §7](02-technical-design-document.md.docx))
- **Stock sync engine** — coalescing buffer + per-channel token bucket + priority queue. Three composable primitives that hold a p99 < 30s sync SLO under flash-sale burst. ([Tech Design §8](02-technical-design-document.md.docx))
- **Persistent webhook idempotency** — Postgres `UNIQUE(channel_id, provider_event_id)` constraint, NOT Redis. Marketplaces redeliver; correctness is non-negotiable. ([Tech Design §9](02-technical-design-document.md.docx))
- **Outbox via EF interceptor** — domain events written atomically with the business transaction, dispatched async (polling → LISTEN/NOTIFY → Debezium CDC migration path documented for scale). ([Tech Design §11](02-technical-design-document.md.docx))
- **Multi-tenant RLS from day one** — `tenant_id` on every row, Postgres Row-Level Security enforced; "the cheapest scale decision in the whole design" per the design doc itself. ([Tech Design §4](02-technical-design-document.md.docx))

## Status

| Phase | Weeks | Deliverable | State |
|---|---|---|---|
| 0 | W0-W2 | Foundation + blessed Inventory module + mock channels + harnesses + CI | ✅ **Complete** ([signoff](docs/phase-gates/2026-05-10-phase-0-signoff.md)) |
| 1 | W3-W5 | Core WMS (Inventory ledger, Inbound, Outbound saga) | Next |
| 2 | W6-W8 | Multi-channel + Sync engine, mechanical service split | Planned |
| 3 | W9-W10 | Real-time + Analytics | Planned |
| 4 | W11-W12 | Harden + Ship | Planned |

## License

License is TBD. Repository is public for portfolio review purposes; please don't redistribute without permission until a license file lands.

## Contact

Author: [longuit2002-blip](https://github.com/longuit2002-blip)
