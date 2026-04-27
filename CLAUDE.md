# ShopFlow WMS — Project Context for AI Assistants

This file is auto-loaded by Claude Code (and respected as a fallback by other agents). It captures the project context that should ship with the source. The user works across multiple computers — anything project-related belongs here in the source tree, not in machine-local memory.

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

- **Cross-machine workflow.** User zips and ships the source between computers. Anything project-related — context, scripts, decisions, ideation, ADRs — must live inside this directory tree, not in `~/.claude/projects/...` or other machine-local locations.
- **Source docs are .docx**, not markdown. Read via the extraction scripts under `tools/`. Treat the .docx as the source of truth; do not edit the extracted .txt as if they were originals.
- **Not yet a git repo.** The first bootstrap action will be `git init` plus the W0 ADRs.

## Recommended next steps (when ready to bootstrap)

1. `git init` and the initial W0 commits per the ideation doc's "Week 0" section.
2. Author `AGENTS.md` (the production rules canon — different from this CLAUDE.md context file) per ideation idea #4.
3. Write `docs/adr/0001-aspire-vs-compose.md` and `docs/adr/0002-modular-monolith-first.md`.
4. Begin Phase 0 deliverables on the modular-monolith foundation.

For deeper development of any single bootstrap idea, run `/compound-engineering:ce-brainstorm`. To produce a day-by-day implementation plan, run `/compound-engineering:ce-plan` with the ideation doc as input.
