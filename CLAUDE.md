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

**Active branch**: `feat/phase-0-redux-db-per-tenant` (cut from `main` after the canon supersession).

**Phase-0-redux progress** (as of 2026-05-12 session 3):
- ✅ U1 — Canon verification + AGENTS.md numbering fix (commit `0111ee7`)
- ✅ U2 — Repo skeleton, sln, props, `global.json` pinning .NET 9 (commit `a26f507`)
- ✅ U3 — Channel test fixtures cherry-picked (commit `31c8a07`)
- ✅ U4 — SharedKernel + 4 analyzers (ShopFlow0001-0004) + 8 passing unit tests; build clean (commit `a9a8c62`)
- ✅ U5 — ControlPlane quartet (Domain/Application/Infrastructure/Migrations) + Tenant aggregate + initial catalog migration + 16 state-machine tests (commit `6307242`)
- ✅ U6 — `shopflow-migrate` CLI: provision/apply/archive/restore/status + 35 unit tests (commit `ee616df`)
- ✅ U7 — Aspire AppHost (Postgres + PgBouncer + Redis + RabbitMQ + observability) + chained provisioning of catalog/dev1/dev2 + production handoff via `infrastructure/docker-compose.yml`
- ⏭️ **U8 — Inventory module (Domain/Application/Infrastructure + initial migration)** (next)
- U9-U10 — pending

**Next implementation step**: resume with `/compound-engineering:ce-work docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md` starting at U8 (Inventory module skeleton — DB-per-tenant schema, no `tenant_id`, `UNIQUE(order_id)` idempotency, repository skeletons throw `NotImplementedException` for Sprint-1-redux to flesh out). Sprint-1-redux follows: [docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md](./docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md).

**U7 deviations from plan file list**: AppHost csproj requires `<Sdk Name="Aspire.AppHost.Sdk" Version="13.3.0" />` (in addition to the `Aspire.Hosting.AppHost` package) — Aspire 13.x's MSBuild SDK is what tells .NET 9's `ImportWorkloads` target the workload is satisfied via NuGet; without it `dotnet build` raises NETSDK1147. CPM bumped `Microsoft.Extensions.Hosting` from 9.0.0 → 10.0.7 (Aspire 13.3.0 transitively requires the 10.x floor; cross-targets net9.0 cleanly). PgBouncer config is generated at AppHost startup from `infrastructure/pgbouncer/pgbouncer.ini.template` and bind-mounted into the bitnami container; the same template ships unmodified to the prod compose manifest. Mock channel servers are reserved as commented placeholders (Phase-2 Sprint-4 deliverable). The `task up` cold-start time + `GET /api/health` tenant-routing scenarios are deferred to U10 sign-off — Inventory.Api lands in U8.

**U5 deviation from plan file list**: `ITenantCatalog` port stays in `ShopFlow.SharedKernel.Application.Ports` (where U4 placed it) rather than being re-created in `ControlPlane.Application.Ports` — the SharedKernel routing middleware and outbox dispatcher consume the port and cannot take a backward dep on ControlPlane. `TenantStatus` enum relocated from `SharedKernel.Application.Ports` to `SharedKernel.Domain` (pure value type) so `ControlPlane.Domain.Tenant` and `Application.Ports.TenantInfo` share one enum without violating layering. The Migrations csproj omits `Microsoft.EntityFrameworkCore.Design` (NU1608 vs CPM-pinned CodeAnalysis 4.11.0); the runtime apply path used by `shopflow-migrate` and U10's smoke test does not need it.

**Session-1 decisions captured** (apply when resuming):
- **D1 PgBouncer pool sizing (U7)**: `pool_mode=transaction`, `default_pool_size=20`, `max_db_connections=20`, `min_pool_size=2`, `reserve_pool_size=5`. Postgres `max_connections=500` in dev, document `1000` for prod.
- **D2 Catalog cache (U5)**: 5 min TTL, LRU size 1000. Synchronous eviction on write paths (provision-complete, archive-start).
- **D3 Migration smoke test assertions (U8/U10)**: load-bearing assertion is `__ef_migrations_history` row count ≥ 1 after `MigrateAsync()`. Per-module named-table existence + named PK / UNIQUE constraint existence. No `pg_dump --schema-only` diff.
- **D4 Routing middleware (already implemented in U4)**: header > JWT > subdomain priority; 2+ source conflict → 403 + audit row; 10 concrete scenarios documented in `TenantRoutingMiddleware.cs`.

**Build/test invariants for resume**:
- `dotnet build` → 0 warnings, 0 errors across 12 projects (9 src + 3 test) including `ShopFlow.AppHost`
- `dotnet test` → 59 passed (8 SharedKernel + 16 ControlPlane state-machine + 35 Migrate unit tests covering arg parsing, module registry, provisioner state transitions, command exit codes)
- .NET 9.0.305 SDK pinned via `global.json`; Aspire AppHost MSBuild SDK 13.3.0 referenced by `ShopFlow.AppHost.csproj`
- Pre-existing csharpier drift on 23 files (mix of LF/CRLF inheritance + line-fold disagreements) carried over from U4-U6 commits; Husky pre-commit hook is not yet installed on this dev machine (`.husky/_/` absent), so commits do not enforce csharpier locally. U10 sign-off should either run `task format` once and capture the cleanup commit or stand up Husky everywhere.

**Always read `docs/CHANGELOG.md` first** to understand what supersedes what. Then `docs/solutions/` for accumulated learnings (re-discovery prevention).

To resume implementation, run `/compound-engineering:ce-work` against the Phase-0-redux plan. To deepen a design decision, run `/compound-engineering:ce-brainstorm` or `/compound-engineering:ce-plan`.
