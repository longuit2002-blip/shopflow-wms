# ShopFlow WMS — Project Context for AI Assistants

This file is auto-loaded by Claude Code (and respected as a fallback by other agents). It captures the project context that should ship with the source. The user works across multiple computers — anything project-related belongs here in the source tree, not in machine-local memory.

> **Two files, two audiences.** [`CLAUDE.md`](./CLAUDE.md) (this file) carries *project context* — what we're building, why, where the source-of-truth specs live, working preferences. [`AGENTS.md`](./AGENTS.md) carries the *executable rule canon* — how to write code in this repo (layering, multi-tenancy, error handling, naming, testing). Both files coexist; they serve different audiences and have different lifecycles. AGENTS.md is the cross-tool standard auto-loaded by Cursor / Codex CLI / Aider / Copilot in addition to Claude Code.

## What this project is

**ShopFlow WMS** — 12-week single-developer portfolio Warehouse Management System for SEA marketplaces (Shopee, Lazada, TikTok Shop, Shopify). Source is being bootstrapped from scratch as of late April 2026.

**Stack**: C# .NET 8, Next.js 14 App Router + React Query + SignalR, Postgres 16 (RLS + monthly partitioning), Redis, RabbitMQ, MassTransit (sagas + outbox), OpenTelemetry, Docker Compose dev. Six modular-monolith microservices internally: Gateway (YARP), Inventory, Inbound, Outbound, Channel, Analytics.

**Engineering anchors**:
- Append-only **reservation ledger** (CTE-based conditional INSERT, not row lock) — the hot-key flash-sale solution.
- **Stock sync engine** with coalescing buffer + per-channel token bucket + priority queue.
- Persistent **webhook idempotency** via Postgres `UNIQUE(channel_id, provider_event_id)` (NOT Redis).
- **Multi-tenant RLS from day 1** even at MVP single-tenant.
- **Outbox pattern** with EF interceptor; dispatcher path: polling → LISTEN/NOTIFY → Debezium CDC at scale.
- **MassTransit saga** for fulfillment orchestration (Reserve → Pick → Pack → Ship with compensation).

## Source documents (canonical)

- [01-product-development-plan.md.docx](./01-product-development-plan.md.docx) — scope, roadmap, SLOs, phase gates, risk register
- [02-technical-design-document.md.docx](./02-technical-design-document.md.docx) — architecture, ADR log, scale-tier roadmap, code excerpts

These are .docx (Word) files. To extract text for grep/search, run [tools/extract-docs.sh](./tools/extract-docs.sh) (bash) or [tools/extract-docs.ps1](./tools/extract-docs.ps1) (PowerShell). The script writes plain-text equivalents to `docs/source/` (gitignored) — re-runnable on any machine.

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

- **Cross-machine workflow.** User zips and ships the source between computers. Anything project-related — context, scripts, decisions, ideation, ADRs, learnings — must live inside this directory tree, not in `~/.claude/projects/...` or other machine-local locations.
- **Source docs are .docx**, not markdown. Read via the extraction scripts under `tools/`. Treat the .docx as the source of truth; do not edit the extracted .txt as if they were originals.
- **Architectural consistency from the start.** All build settings live in [`Directory.Build.props`](./Directory.Build.props); all package versions in [`Directory.Packages.props`](./Directory.Packages.props) (CPM enforced); test conventions in [`tests/Directory.Build.props`](./tests/Directory.Build.props). Per-csproj content is only what diverges. Module shape is canon — see [`AGENTS.md`](./AGENTS.md) §11.
- **Compounding learnings**: when a fix is non-obvious, capture it in [`docs/solutions/`](./docs/solutions/) so it doesn't bite a third time. Triggered the "every reviewer comment is a missing rule" pattern.

## Repo layout

```
.
├── AGENTS.md                      AI-pair-programming rule canon (auto-loaded by Claude/Cursor/Copilot)
├── CLAUDE.md                      this file (project context)
├── Directory.Build.props          repo-wide csproj defaults
├── Directory.Packages.props       Central Package Management — every package version
├── README.md                      public-facing front door
├── ShopFlow.sln                   .NET solution
├── 01-product-development-plan.md.docx
├── 02-technical-design-document.md.docx
├── docs/
│   ├── adr/                       numbered architectural decisions (immutable)
│   ├── ideation/                  ranked candidate ideas (input to plans)
│   ├── plans/                     active work plans with U-IDs
│   ├── solutions/                 compounding learnings (re-discovery prevention)
│   └── source/                    .docx → .txt extracts (gitignored)
├── infrastructure/                (placeholder for U7/U9 — mock-channels, compose)
├── src/
│   ├── Services/                  bounded-context modules (Inventory blessed in U6)
│   └── Shared/                    SharedKernel + Analyzers + Contracts
├── tests/
│   ├── fixtures/channels/         Shopee+Lazada synthetic-but-realistic payloads
│   ├── Directory.Build.props      test conventions (Xunit + FluentAssertions implicit usings)
│   └── ShopFlow.*UnitTests | *IntegrationTests/
└── tools/
    ├── extract-docs.{sh,ps1}      .docx text extraction
    └── (shopflow-gate lands in U11)
```

## Current stage

Phase 0 (bootstrap) on `feat/phase-0-bootstrap`. The active plan is [`docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md`](./docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md). U1–U6 shipped (W0 + most of W1). U7 (mock-channel servers) is next.

To resume work, run `/compound-engineering:ce-work` and point it at the plan. The plan, AGENTS.md, ADRs, and docs/solutions/ are the durable inputs — every fresh agent session reads them automatically.
