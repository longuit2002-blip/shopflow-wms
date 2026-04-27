---
title: "feat: ShopFlow WMS Phase 0 bootstrap (modular-monolith stance, W6 split planned)"
type: feat
status: active
date: 2026-04-27
origin: docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md
---

# feat: ShopFlow WMS Phase 0 bootstrap (modular-monolith stance, W6 split planned)

## Overview

Stand up the ShopFlow WMS source code from a fresh repo to a state where Phase-1 Sprint-1 (the reservation ledger) can begin against passing scale gates. The plan covers Week 0 (decisions and scaffolding before any service code), Week 1 (the blessed Inventory module + cross-cutting NuGet + mock-channel server + test-first harnesses + CI), and Week 2 (replicate module shape × 5, harden, validate Phase-0 scale gates).

The architectural stance is **modular-monolith first** (one .NET solution, six logical modules in separate `.csproj` per bounded context, single host with in-memory MediatR) with a **planned mechanical split at W6** when the channel adapter framework arrives and async cross-process messaging actually pays its freight. This is a deliberate inversion of "6 services from W1" — the ideation document `docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md` argues (with citations from Tech Design §6) that microservices for a 12-week solo build are a portfolio-narrative cost paid against engineering judgment.

---

## Problem Frame

ShopFlow WMS is a 12-week single-developer portfolio Warehouse Management System for SEA marketplaces (Shopee, Lazada, TikTok Shop, Shopify). The product spec is `01-product-development-plan.md.docx`; the technical design is `02-technical-design-document.md.docx`. Both define a 5-phase roadmap with hard scale gates per phase. **Phase 0 (Weeks 1-2 in the design doc's numbering, but Week 0 + Weeks 1-2 here including pre-W1 decision work)** ships the foundation: solution layout, shared kernel, gateway, Docker Compose dev stack with all infra (Postgres, Redis, RabbitMQ, observability, mock channels), JWT auth, health endpoints, CI pipeline.

The ideation produced 7 ranked bootstrap ideas. All 7 are mutually compatible and form a coherent stance — this plan converts them into 12 implementation units across 3 phases (W0 / W1 / W2). The plan does not implement Phase 1+ work; that is handled by future plans grounded in this one.

The user works across multiple computers via zip+ship, so all project artifacts must live in-tree (CLAUDE.md captures this constraint, see origin: `CLAUDE.md`).

---

## Requirements Trace

- R1. Phase 0 produces a deployable, observable, tenant-aware modular monolith ready for the Phase-1 Sprint-1 reservation-ledger scale gate (5,000 concurrent reservations against 1,000 units → exactly 1,000 successes, p99 < 200ms, zero oversell — Plan §299).
- R2. All cross-cutting concerns (OTel + W3C TraceContext, EF Core tenancy `SaveChangesInterceptor`, outbox interceptor + dispatcher, MassTransit bus defaults, `Result<T>`, `BaseEntity`, `IRequestContext`) live in one shared meta-package referenced by every module via a single `services.AddShopFlowDefaults()` call (origin: ideation #5).
- R3. Tenant isolation is compile-time enforced via a Roslyn analyzer bundled in the shared meta-package — RLS at the DB layer is necessary but insufficient (origin: ideation #6, Tech Design §4.5).
- R4. Mock-channel server is forward-deployed in the dev orchestrator from Day 1 with HMAC verification, configurable failure injection via HTTP control plane, and at least 5 named failure scenarios; every module wires against the mock from W1 (origin: ideation #3, Plan §348).
- R5. Reservation ledger and stock-sync invariants are codified as property/load tests against `NotImplementedException` stubs in W1, before any implementation lands (origin: ideation #7, Plan §299, §316-323).
- R6. AGENTS.md exists at repo root with a curated rule set (~180 instruction budget) pointing at the Inventory module as the blessed reference; the file is committed before any service code (origin: ideation #4).
- R7. Phase-0 scale gate is met by end of W2: cold-start (`aspire run` or `docker compose up --wait`) < 90s, auth happy-path p99 < 150ms, CI pipeline end-to-end < 10 min (Plan §293).
- R8. The W6 mechanical split into 6 service processes is a planned event with its own scale gate, declared in ADR-0002 (origin: ideation #1, Tech Design §6).
- R9. All project context, scripts, decisions, and artifacts live inside the repo so the source can be zipped and shipped across machines without losing context (origin: `CLAUDE.md`).
- R10. The repo's first commit is already on `main` at github.com/longuit2002-blip/shopflow-wms; this plan adds work on top, with each unit landing as one or a small cluster of commits.

---

## Scope Boundaries

- The reservation ledger SQL (CTE conditional INSERT) and its full domain code are scaffolded in U6 but the **5,000-concurrent scale gate is Phase-1 Sprint-1's responsibility**, not Phase 0's.
- Stock sync engine primitives (coalescing, token bucket, priority queue) are stubbed with `NotImplementedException` in W1 (U8); their implementation is Phase-2 Sprint-5.
- MassTransit saga implementation is deferred to Phase-1 Sprint-3 (Outbound). W1 only wires saga state-machine registration and bus configuration into the meta-package.
- Real Shopee/Lazada API integration is **never** in scope for the 12-week build. Mocks are the engineering (Plan §348).
- Frontend (Next.js 14) is not part of Phase 0; it lands in Phase 1+ when there is a domain to render.
- Production deployment manifests (k8s, ECS, Nomad) are deferred — Phase 0 ships dev orchestrator only (Aspire AppHost or docker-compose).

### Deferred to Follow-Up Work

- **Phase 1 (W3-5) sprint plans**: Inventory ledger implementation, Inbound flow, Outbound saga. Each gets its own plan grounded in Phase 0's foundation.
- **W6 mechanical split**: extracting modules into independent service processes. ADR-0002 commits to the date and the gate; the actual split is its own plan in W6.
- **Phase 2-4 plans** (Multi-Channel + Sync, Real-time + Analytics, Harden + Ship).
- **Public-facing README.md**: noted in U4 as a stub; richer marketing-grade README is a tactical follow-up after Phase 0 sign-off.

---

## Context & Research

### Relevant Code and Patterns

The repo is greenfield (one initial commit with planning artifacts only). No prior code patterns to follow internally. Patterns used here are imported from the design docs and external grounding.

- `02-technical-design-document.md.docx` §5 — solution layout (4-layer Clean Architecture per service, project reference rules)
- `02-technical-design-document.md.docx` §7 — reservation ledger SQL + covering index pattern
- `02-technical-design-document.md.docx` §8 — stock sync three primitives (coalescing, token bucket, priority queue)
- `02-technical-design-document.md.docx` §9 — webhook idempotency via Postgres UNIQUE constraint
- `02-technical-design-document.md.docx` §10 — MassTransit fulfillment saga state machine
- `02-technical-design-document.md.docx` §11 — outbox + EF interceptor + LISTEN/NOTIFY → CDC migration path
- `02-technical-design-document.md.docx` §16 — observability (OTel + W3C TraceContext + correlation ID)
- `02-technical-design-document.md.docx` §20 — shared kernel (BaseEntity, ValueObject, Result<T>, ITenantContext, IDomainEvent)
- `CLAUDE.md` — project context, working preferences, cross-machine workflow constraint

### Institutional Learnings

`docs/solutions/` does not exist yet. The ideation document `docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md` is the closest substitute and is the primary origin for this plan.

### External References

Captured during ideation; not re-fetched for this plan:

- `dotnet/eShop` (github.com/dotnet/eShop) — Microsoft's current .NET 9 reference e-commerce app with Aspire orchestration, no MassTransit, EF-based outbox. Used as input to ADR-0001 (Aspire vs Compose).
- `MassTransit/Sample-Outbox` (github.com/MassTransit/Sample-Outbox) — canonical EF outbox wiring with Postgres + EF Core. Pattern adopted in U5.
- Ardalis CleanArchitecture v11 (github.com/ardalis/CleanArchitecture) — single-service Clean Architecture template; structure adopted, single-service shape rejected.
- Mehmet Ozkaya `aspnetrun-microservices` — closest public analog to the stack (RabbitMQ + MassTransit + YARP + Postgres + Compose).
- AGENTS.md cross-tool standard — root + per-service hierarchy with ~150-200 instruction budget (deployhq.com, humanlayer.dev, builder.io references).
- Husky.NET + CSharpier as the .NET pre-commit baseline; Taskfile (`task`) as the cleanest .NET monorepo task runner.
- Aspire 13 GA (2025-2026) — replaces docker-compose for local dev with `aspire run`; Compose-output for deployment is "in progress." Input to ADR-0001.

---

## Key Technical Decisions

- **Modular monolith first; mechanical split at W6**: One `.sln`, six `.csproj` quartets, single host with in-memory MediatR. Tenant_id, RLS, outbox semantics wired from commit 1. Async cross-process messaging waits until the channel adapter framework demands it (origin: ideation #1, Tech Design §6).
- **Aspire AppHost as the recommended candidate for local dev orchestration**, validated or rejected by ADR-0001 in U1. Rationale: Aspire 13 GA, dotnet/eShop alignment, free OTel dashboard. Risk: Compose-output for deployment is "in progress." If ADR concludes Compose, the cold-start gate (`docker compose up --wait` < 90s) is enforced as a CI check from PR #1 (origin: ideation #2).
- **One cross-cutting NuGet meta-package, no `dotnet new` template generator**: At N=6 modules the generator's 2-3 day cost never breaks even against ~2 hours of copy-paste. The package is the upgrade path; copy-paste is the per-module skeleton (origin: ideation #5, anti-promoting F2.2).
- **Roslyn analyzer bundled in the meta-package, Warning mode in W1, Error mode in W2**: Ships in Warning during the canon-stabilization period, promotes to Error after the canon settles. Rules: no raw `DbSet<T>` queries (must go through tenant-scoped repository), `IPublishEndpoint` calls must propagate correlation context, webhook handlers require `[Idempotent]` attribute, `DateTime.Now` is forbidden (origin: ideation #6).
- **Mock-channel server is a forward-deployed first-class service in the dev orchestrator**, not test scaffolding. Built BEFORE service #1 (technically, before any module beyond the meta-package) so every module wires against it from day one (origin: ideation #3).
- **Test-first the reservation ledger and stock-sync engine in W1 against `NotImplementedException` stubs**: The harnesses are the spec. Assertions are quoted directly from Plan §299 and §316-323 (origin: ideation #7).
- **AGENTS.md is the executable canon for AI-pair-programming**, distinct from CLAUDE.md (project context for human reading). Both files coexist; they serve different audiences. AGENTS.md points to the Inventory module as the blessed reference once it lands at end of W1 (origin: ideation #4).
- **ADRs are the durable decision artifacts**, alongside (not replaced by) AGENTS.md: ADR-0001 and ADR-0002 in W0; subsequent ADRs as the project commits to architectural choices. AGENTS.md captures the rules, ADRs capture the *why* of permanent decisions.

---

## Open Questions

### Resolved During Planning

- **Q: Which orchestrator — Aspire 13 or Docker Compose?** A: U1 ADR-0001 makes the call. Plan recommends Aspire AppHost as the candidate to beat (ideation external grounding); ADR may conclude either way. CI cold-start gate adapts to the chosen orchestrator.
- **Q: Should the Roslyn analyzer ship as a separate NuGet?** A: No, bundled with the cross-cutting meta-package (`ShopFlow.SharedKernel`) so consumers cannot adopt the kernel without the canon enforcement (origin: ideation #5+#6).
- **Q: What goes in the mock-channel server v1?** A: Happy-path order and stock endpoints + HMAC verification + control-plane HTTP API for failure injection + 5 named YAML scenarios: `429-with-weird-retry-after`, `webhook-redelivered-after-200-ack`, `signature-clock-skew-3min`, `partial-body-then-eof`, `5xx-burst-30s`. Real-fixture seed comes from U3.
- **Q: Real-fixture spike — which marketplace and how long?** A: Both Shopee and Lazada in a single 1-day spike (U3). Save under `tests/fixtures/channels/{shopee,lazada}/`. Even reading public docs and copying example payloads is enough to seed the catalog with realistic shapes.
- **Q: When does the Roslyn analyzer promote from Warning to Error?** A: U10 — W2 Day 3, after one full sprint of the canon stabilizing across all 6 module skeletons. Promotion is gated on zero false positives in the prior 7 days.
- **Q: Does the W0 ADR sequence land in one PR or two?** A: U1 covers both ADRs as one unit (one PR is fine; ADRs are written together since ADR-0001 affects ADR-0002's prescribed dev orchestrator).

### Deferred to Implementation

- **Exact Roslyn analyzer NuGet package SDK and `MSBuild` glue**: The implementer (or a future `ce-work` session) will look up `Microsoft.CodeAnalysis.CSharp.Workspaces` + `Analyzers` SDK current 2026 conventions when authoring U5. Plan-time decision is "ship analyzer in the meta-package"; implementation-time decision is "exactly which SDK template."
- **Aspire AppHost specific resource registration syntax for the mock-channel servers**: If Aspire wins the ADR, U7 will register the mocks as Aspire resources; the exact API call is implementation-detail.
- **CI runner choice (GitHub Actions vs self-hosted)**: U9 plans CI as GitHub Actions by default (lowest-friction for a public repo). If the load-test stage exceeds free-tier minutes, a self-hosted runner is added in a follow-up plan.
- **Initial Postgres image tag and version-pinning policy**: U5 pins `postgres:16` tag (matching Tech Design §1 explicit version). Specific point release (e.g., `postgres:16.4-alpine`) is decided at the point of writing the docker-compose.yml or AppHost.cs, with `IMAGE_PINS.md` documenting the policy.
- **Saga state-machine YAML/DSL codegen** (rejected in ideation as too expensive at ~4 sagas): saga state machines will be hand-written in C# per Tech Design §10.3. Decision is recorded so a future plan does not reintroduce this question.

---

## Output Structure

The Phase 0 bootstrap creates this directory tree (greenfield — every directory below `src/`, `tests/`, `infrastructure/`, `docs/adr/` is new). The `docs/`, `tools/`, root `CLAUDE.md`, and `.git*` files already exist from the initial commit.

```text
shopflow-wms/                                       (existing repo at github.com/longuit2002-blip/shopflow-wms)
├── .editorconfig                                   (NEW — U4)
├── .gitattributes                                  (existing)
├── .gitignore                                      (existing; extended in U4 for .NET output)
├── .github/
│   └── workflows/
│       ├── ci.yml                                  (NEW — U9)
│       └── chaos-nightly.yml                       (NEW — U9, stub initially)
├── AGENTS.md                                       (NEW — U2; AI-pair-programming rule canon)
├── CLAUDE.md                                       (existing; updated in U2 to point at AGENTS.md)
├── README.md                                       (NEW — U4; stub with elevator pitch + links)
├── Taskfile.yml                                    (NEW — U4; cross-platform task runner)
├── ShopFlow.sln                                    (NEW — U6)
├── 01-product-development-plan.md.docx             (existing)
├── 02-technical-design-document.md.docx            (existing)
│
├── docs/
│   ├── adr/
│   │   ├── 0001-aspire-vs-docker-compose.md        (NEW — U1)
│   │   └── 0002-modular-monolith-first.md          (NEW — U1)
│   ├── ideation/
│   │   └── 2026-04-27-shopflow-wms-bootstrap-ideation.md  (existing)
│   ├── plans/
│   │   └── 2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md  (this file)
│   └── source/                                     (existing, gitignored — derived .docx text)
│
├── infrastructure/
│   ├── docker-compose.yml                          (NEW — U9; conditional on ADR-0001)
│   ├── docker-compose.override.yml                 (NEW — U9; local-dev overrides)
│   └── mock-channels/
│       ├── shopee-mock/                            (NEW — U7)
│       └── lazada-mock/                            (NEW — U7)
│
├── src/
│   ├── AppHost/                                    (NEW — U9; conditional on ADR-0001 = Aspire)
│   │   └── ShopFlow.AppHost.csproj
│   │
│   ├── ApiGateway/
│   │   └── ShopFlow.Gateway/                       (NEW — U10; YARP gateway, deferred shape until W2)
│   │
│   ├── Services/
│   │   ├── Inventory/                              (NEW — U6; the blessed reference module)
│   │   │   ├── ShopFlow.Inventory.Domain/
│   │   │   ├── ShopFlow.Inventory.Application/
│   │   │   ├── ShopFlow.Inventory.Infrastructure/
│   │   │   ├── ShopFlow.Inventory.Api/
│   │   │   └── AGENTS.md                           (NEW — U6; module-specific deltas)
│   │   ├── Inbound/                                (NEW — U10; quartet replicated from Inventory)
│   │   ├── Outbound/                               (NEW — U10)
│   │   ├── Channel/                                (NEW — U10)
│   │   └── Analytics/                              (NEW — U10)
│   │
│   └── Shared/
│       ├── ShopFlow.SharedKernel/                  (NEW — U5; cross-cutting NuGet meta-package)
│       │   ├── Domain/
│       │   ├── Application/
│       │   ├── Infrastructure/
│       │   └── Analyzers/                          (NEW — U5; Roslyn analyzer project)
│       └── ShopFlow.Contracts/                     (NEW — U5; integration event shapes)
│
├── tests/
│   ├── fixtures/
│   │   └── channels/
│   │       ├── shopee/                             (NEW — U3; recorded webhook + API payloads)
│   │       └── lazada/                             (NEW — U3)
│   ├── ShopFlow.Inventory.UnitTests/               (NEW — U6)
│   ├── ShopFlow.Inventory.IntegrationTests/        (NEW — U6; Testcontainers)
│   ├── ShopFlow.PropertyTests/                     (NEW — U8; FsCheck reservation ledger spec)
│   ├── ShopFlow.LoadTests/                         (NEW — U8; k6 / NBomber sync primitives spec)
│   └── ShopFlow.SharedKernel.UnitTests/            (NEW — U5)
│
└── tools/
    ├── extract-docs.sh                             (existing)
    ├── extract-docs.ps1                            (existing)
    └── shopflow-gate/                              (NEW — U11; phase-gate harness CLI)
        └── ShopFlow.Gate.csproj
```

This is a scope declaration showing expected output shape. Implementer may adjust if implementation reveals a better layout (e.g., Aspire output project naming conventions). Per-unit `**Files:**` sections remain authoritative.

---

## High-Level Technical Design

> *This illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce.*

The Phase-0 system shape, end of W2:

```text
                ┌──────────────────────────────────────────────────────┐
                │  Dev orchestrator: Aspire AppHost OR docker-compose  │
                │  (one decided in ADR-0001 — U1)                      │
                └─┬──────────┬──────────┬──────────┬──────────┬─────────┘
                  │          │          │          │          │
                  ▼          ▼          ▼          ▼          ▼
              Postgres    Redis    RabbitMQ    Tempo    shopee-mock + lazada-mock
              (16)        (7)      (3)         (OTel)   (Express servers, U7)
                  ▲                                          ▲
                  │                                          │
                  │ EF Core w/ tenancy interceptor + outbox  │ HTTP w/ HMAC + control plane
                  │                                          │
        ┌─────────┴──────────────────────────────────────────┴─────────┐
        │   Single-host modular monolith (one .NET process)            │
        │                                                              │
        │   ┌───────────┐  ┌─────────┐  ┌──────────┐  ┌────────────┐   │
        │   │ Inventory │  │ Inbound │  │ Outbound │  │  Channel   │   │
        │   │ (blessed) │  │         │  │ + Saga   │  │ + Sync     │   │
        │   │  (U6)     │  │  (U10)  │  │  (U10)   │  │  (U10)     │   │
        │   └─────┬─────┘  └────┬────┘  └────┬─────┘  └─────┬──────┘   │
        │         │             │            │              │          │
        │         └─────────┬───┴────────────┴──────────────┘          │
        │                   │ in-memory MediatR (W1-W5)                │
        │                   │ → MassTransit RabbitMQ (W6 split)        │
        │         ┌─────────┴──────────────────────┐                   │
        │         │ ShopFlow.SharedKernel (U5)     │                   │
        │         │ • OTel + W3C TraceContext       │                  │
        │         │ • EF tenancy/outbox interceptors│                  │
        │         │ • MassTransit defaults          │                  │
        │         │ • Result<T>, BaseEntity         │                  │
        │         │ • IRequestContext               │                  │
        │         │ • Roslyn analyzer (U5; W1=Warn, U11=Error)         │
        │         └────────────────────────────────┘                   │
        │   ┌────────────────┐                                         │
        │   │ Analytics (read)│  ┌────────────────┐                    │
        │   │   (U10)         │  │ ApiGateway     │                    │
        │   └────────────────┘  │ (YARP, U10)    │                    │
        │                       └────────────────┘                     │
        └──────────────────────────────────────────────────────────────┘
                  ▲                                          ▲
                  │ phase-gate CLI invokes load + chaos      │ FsCheck property tests
                  │                                          │ k6/NBomber load tests
                  │ tools/shopflow-gate (U11)                │ tests/ShopFlow.PropertyTests + LoadTests (U8)
                  │                                          │
                  └──────────────────────────────────────────┘
                       Phase-0 sign-off: U12 verifies cold-start, auth p99, CI <10min
```

**Sequence of W6 mechanical split (declared in ADR-0002, executed in a future plan):**

```text
W1-W5:  one host, in-memory bus, modules talk via MediatR
            │
            │ ADR-0002 trigger: channel adapter framework (Phase-2 Sprint-4) demands async messaging
            ▼
W6:     mechanically split — each module's .Api project becomes its own host;
        MassTransit transport flips from in-memory to RabbitMQ;
        the cross-process scale gate runs to validate split did not regress correctness
```

---

## Implementation Units

Units are organized into 3 phases (W0, W1, W2) for clarity. U-IDs are stable and never renumbered.

### Phase A — Week 0 (decisions and scaffolding before any service code)

- U1. **W0 ADRs (ADR-0001 Aspire-vs-Compose, ADR-0002 Modular-Monolith-First)**

**Goal:** Lock the two foundational architectural decisions in writing before any code is structured around them. ADR-0001 settles dev orchestration; ADR-0002 settles deployment topology and the W6 split commitment.

**Requirements:** R7, R8

**Dependencies:** None

**Files:**
- Create: `docs/adr/0001-aspire-vs-docker-compose.md`
- Create: `docs/adr/0002-modular-monolith-first.md`

**Approach:**
- ADR-0001 follows the Context / Decision / Rationale / Consequences / When this breaks pattern from Tech Design §1. Recommended decision: Aspire AppHost for local dev with Compose generated as a deployment artifact. Engages explicitly with the no-cloud-lock-in constraint (Tech Design §165) and Aspire's "in-progress" Compose-output story. Captures the cold-start gate ( < 90s ) as the verification criterion regardless of which side wins.
- ADR-0002 documents the modular-monolith-first stance, citing Tech Design §6 verbatim ("does a 12-week portfolio project earn six microservices?") and committing to the W6 mechanical split as a planned event with its own scale gate (cross-process correctness regression check). Also documents what the README opens with: the eventual 6-service diagram with a "Phase 0-1 = modular monolith stage" label.
- ADRs are immutable once accepted; reversal is a new ADR that supersedes by link.

**Patterns to follow:** Tech Design §1 ADR table format. Numbered, dated, status field (Accepted | Superseded by ADR-NNNN).

**Test scenarios:** Test expectation: none — pure documentation, no behavioral change. Reviewer review is the only "test."

**Verification:**
- Both files exist at the specified paths.
- Each ADR has Context / Decision / Rationale / Consequences / When-this-breaks sections.
- ADR-0002 explicitly names the W6 trigger (channel adapter framework arrival) and the scale gate (cross-process correctness regression).

---

- U2. **Root AGENTS.md (executable canon for AI-pair-programming)**

**Goal:** Author the canonical rule file that future AI-pair-programming sessions (Claude Code, Cursor, Copilot) anchor against, with a hard ~180-instruction budget. Capture the non-negotiables before any code lands so every service skeleton inherits the canon by default.

**Requirements:** R6, R9

**Dependencies:** U1 (ADR-0002 informs the modular-monolith stance referenced by AGENTS.md)

**Files:**
- Create: `AGENTS.md`
- Modify: `CLAUDE.md` (add a one-line cross-reference to AGENTS.md so the relationship is explicit — CLAUDE.md = project context for humans; AGENTS.md = rule canon for AI helpers)

**Approach:**
- Codify the non-negotiables: `tenant_id` on every table, outbox-or-don't-publish, `Result<T>` for failures, idempotency-key on every webhook handler, no `DateTime.Now`, no raw `DbSet<T>` queries (must go through tenant-scoped repository), Result-pattern instead of exceptions for domain failures, naming conventions (PascalCase for types, suffix `Aggregate`/`ValueObject`/`DomainEvent`), MediatR pipeline order, async/await rules, "when in doubt, copy the pattern from `src/Services/Inventory/`" (Inventory becomes the blessed reference at end of W1 in U6).
- ~180-instruction budget is hard. Each rule is one short sentence. Group by category: layering, data access, error handling, naming, async, AI workflow.
- Per-service AGENTS.md stubs (one per module) added in U6 (Inventory) and U10 (the other 5).
- The "blessed reference" pointer is a forward declaration — the reference file does not yet exist when AGENTS.md is committed; the pointer becomes valid when U6 lands.

**Patterns to follow:** AGENTS.md cross-tool standard convention (root + per-service hierarchy, ~150-200 instruction budget). Reviewer-comment-as-missing-rule discipline noted in the file's own preamble.

**Test scenarios:** Test expectation: none — documentation. The "test" is whether downstream AI-assisted PRs follow the canon; that is verified by the Roslyn analyzer (U5) and code review.

**Verification:**
- File exists at repo root.
- Total instruction count ≤ 200 (verifiable by counting numbered/bulleted lines in the rules section).
- CLAUDE.md cross-reference added.

---

- U3. **Real-fixture spike: capture Shopee + Lazada webhook payloads and sample API responses**

**Goal:** Seed the mock-channel server (U7) with real-shape webhook payloads and API response bodies from Shopee and Lazada developer documentation, so the mock catalog from day one reflects real wire format rather than imagined shapes.

**Requirements:** R4

**Dependencies:** None

**Files:**
- Create: `tests/fixtures/channels/shopee/webhook-order-created.json`
- Create: `tests/fixtures/channels/shopee/webhook-order-cancelled.json`
- Create: `tests/fixtures/channels/shopee/api-product-list-response.json`
- Create: `tests/fixtures/channels/shopee/README.md` (source attribution + fingerprints of fields)
- Create: `tests/fixtures/channels/lazada/webhook-order-status.json`
- Create: `tests/fixtures/channels/lazada/api-product-list-response.json`
- Create: `tests/fixtures/channels/lazada/README.md`

**Approach:**
- Read public Shopee Open Platform documentation and Lazada Open Platform documentation. Capture at least 2 webhook payload examples and 1 API response example per marketplace.
- Anonymize any real merchant IDs, signatures, tokens — replace with `EXAMPLE_*` placeholders.
- Each marketplace folder's `README.md` documents the source URL + retrieval date + which fields are real-shape vs synthetic placeholders.
- One day total budget. If real examples are not findable in public docs, generate synthetic but plausible payloads matching the documented schema and clearly mark them in the README.

**Patterns to follow:** None internal. External references: Shopee Open Platform docs, Lazada Open Platform docs.

**Test scenarios:** Test expectation: none — fixtures are inputs to other tests, not behavior.

**Verification:**
- All 7 files exist.
- Each marketplace `README.md` documents source + date + real-vs-synthetic per field.
- All sensitive-looking values use `EXAMPLE_*` placeholders.

---

- U4. **Repo skeleton: Taskfile, .editorconfig, README.md, pre-commit hooks**

**Goal:** Stand up the cross-machine, cross-platform developer-experience baseline so `task setup` after a fresh clone produces a working environment in under 60 seconds.

**Requirements:** R7, R9

**Dependencies:** None

**Files:**
- Create: `Taskfile.yml` (root)
- Create: `.editorconfig`
- Create: `README.md` (stub: elevator pitch + repo layout pointer + "what stage we're in" + link to ideation and plan docs)
- Create: `.husky/_/pre-commit` and `.husky/pre-commit` (Husky.NET layout)
- Create: `package.json` only if Husky.NET requires it; otherwise pure-.NET install via `dotnet tool install Husky --global` documented in Taskfile
- Modify: `.gitignore` (add .NET bin/, obj/, *.user — already partially done in initial commit)

**Approach:**
- Taskfile.yml exposes: `task setup` (idempotent install of dependencies + dotnet tool restore), `task up` (start dev orchestrator), `task down` (stop), `task test`, `task ci` (run the full CI sequence locally), `task pre-commit` (formatter + analyzer Warning-mode), `task migrate` (EF migrations once they exist).
- Husky.NET wires the pre-commit hook to invoke `dotnet csharpier .` (formatter) on staged files. CSharpier is non-configurable by design — adoption is binary.
- README.md stub is intentionally minimal: 200-400 words. Lead paragraph names the project, the 12-week scope, the modular-monolith stance, and links to docs/. Does not yet contain feature screenshots or marketing copy — those come at Phase 4 ship.
- .editorconfig pins UTF-8, LF for `.cs`, CRLF for `.ps1` (matches .gitattributes), 4-space indent for C#, 2-space for YAML/JSON.

**Patterns to follow:** Taskfile.yml conventions (https://taskfile.dev). Husky.NET + CSharpier .NET pre-commit baseline.

**Test scenarios:**
- Edge case: `task setup` is idempotent — running it twice on a clean clone produces the same state both times, no errors.
- Edge case: `task pre-commit` rejects a staged file with deliberately bad formatting (e.g., unsorted usings, wrong indent) and accepts it after `dotnet csharpier .`.
- Integration: from a fresh clone (`git clone` to a new directory), `task setup` completes in < 60s on a developer laptop with .NET 8 SDK pre-installed.

**Verification:**
- `task setup` succeeds from a fresh clone.
- `task --list` shows all defined tasks.
- Pre-commit hook fires on `git commit` and runs CSharpier.
- README.md renders cleanly on github.com/longuit2002-blip/shopflow-wms.

---

### Phase B — Week 1 (build the meta-package, blessed reference module, mock-channels, harnesses, CI)

- U5. **`ShopFlow.SharedKernel` cross-cutting NuGet meta-package (with bundled Roslyn analyzer in Warning mode)**

**Goal:** Ship the single referenced NuGet that every module pulls in for cross-cutting wiring. One `services.AddShopFlowDefaults(IConfiguration)` call configures Serilog + OTel + W3C TraceContext, EF Core tenancy `SaveChangesInterceptor`, outbox interceptor + dispatcher, MassTransit bus defaults, health endpoints, Swagger, problem-details middleware, and registers the analyzer rules. Includes shared types (`Result<T>`, `BaseEntity`, `AggregateRoot`, `ValueObject`, `IRequestContext`, `IDomainEvent`).

**Requirements:** R2, R3

**Dependencies:** U1 (ADR-0001 informs MassTransit transport default — in-memory until W6 split per ADR-0002), U4 (Taskfile to run the kernel's tests)

**Files:**
- Create: `src/Shared/ShopFlow.SharedKernel/ShopFlow.SharedKernel.csproj`
- Create: `src/Shared/ShopFlow.SharedKernel/Domain/BaseEntity.cs`, `AggregateRoot.cs`, `ValueObject.cs`, `Result.cs`, `IDomainEvent.cs`
- Create: `src/Shared/ShopFlow.SharedKernel/Application/IRequestContext.cs`, `RequestContext.cs`, `MediatR pipeline behaviors (validation, logging, tracing)`
- Create: `src/Shared/ShopFlow.SharedKernel/Infrastructure/TenancyInterceptor.cs`, `OutboxInterceptor.cs`, `OutboxDispatcher.cs` (polling mode for MVP per Tech Design §11.3 Mode A)
- Create: `src/Shared/ShopFlow.SharedKernel/Infrastructure/AddShopFlowDefaults.cs` (composition root extension method)
- Create: `src/Shared/ShopFlow.SharedKernel/Analyzers/ShopFlowAnalyzers.csproj` (separate Roslyn analyzer project; ships in the same NuGet as analyzer assets)
- Create: `src/Shared/ShopFlow.SharedKernel/Analyzers/RawDbSetAnalyzer.cs`, `MissingCorrelationAnalyzer.cs`, `MissingIdempotentAnalyzer.cs`, `DateTimeNowAnalyzer.cs`
- Create: `src/Shared/ShopFlow.SharedKernel/AGENTS.md` (module-level deltas: "this is the canon foundation; rule changes require ADR")
- Create: `src/Shared/ShopFlow.Contracts/ShopFlow.Contracts.csproj` (integration event shapes — initially empty per-event records added in U6 and U10)
- Create: `tests/ShopFlow.SharedKernel.UnitTests/ShopFlow.SharedKernel.UnitTests.csproj`
- Test: `tests/ShopFlow.SharedKernel.UnitTests/Domain/ResultTests.cs`, `BaseEntityTests.cs`, `ValueObjectTests.cs`
- Test: `tests/ShopFlow.SharedKernel.UnitTests/Infrastructure/TenancyInterceptorTests.cs`, `OutboxInterceptorTests.cs`
- Test: `tests/ShopFlow.SharedKernel.UnitTests/Analyzers/RawDbSetAnalyzerTests.cs`, `MissingCorrelationAnalyzerTests.cs`, `MissingIdempotentAnalyzerTests.cs`, `DateTimeNowAnalyzerTests.cs`

**Approach:**
- Domain types are reference implementations of Tech Design §20 (BaseEntity, ValueObject, Result, ITenantContext, IDomainEvent) with no framework dependencies.
- TenancyInterceptor reads `IRequestContext.TenantId`, sets `tenant_id` on inserts, applies `WHERE tenant_id = @t` filter on queries via EF global query filter. Falls back to a thrown exception if `IRequestContext` is unavailable in scope (forces explicit context propagation).
- OutboxInterceptor follows MassTransit Sample-Outbox pattern: collects domain events from tracked aggregates during `SavingChangesAsync`, writes them to `outbox_messages` table in the same transaction.
- OutboxDispatcher is a `BackgroundService` that polls every 500ms (Mode A from Tech Design §11.3); LISTEN/NOTIFY upgrade is a follow-up plan.
- Analyzers ship in Warning mode by default. Each analyzer has a `ShopFlow0001` … `ShopFlow0004` diagnostic ID. Severity is configured via the consumer's `.editorconfig` so individual modules can promote-to-Error at their pace; default ruleset bundled in the package configures all four as Warning.
- Tests for analyzers use `Microsoft.CodeAnalysis.Testing` framework: each test feeds a small C# snippet, asserts on diagnostic count, ID, severity, and span.

**Execution note:** The four analyzers are written test-first per the cited TDD-for-concurrency reasoning in ideation #7. Each analyzer's failing-input test and passing-input test are written before the analyzer's code.

**Patterns to follow:** MassTransit Sample-Outbox (github.com/MassTransit/Sample-Outbox) for outbox wiring. Microsoft.CodeAnalysis.Testing samples for analyzer tests. Tech Design §20 for shared kernel types.

**Test scenarios:**
- Happy path: `services.AddShopFlowDefaults(config)` registers OTel, MassTransit, EF interceptors, MediatR pipeline behaviors; resolved `IRequestContext` is non-null when called from inside an HTTP request.
- Happy path: `Result<T>.Success(v)` and `Result<T>.Failure(err)` round-trip correctly through `Match()`.
- Happy path: domain event raised on aggregate is collected by `OutboxInterceptor.SavingChangesAsync` and persisted to `outbox_messages` in the same transaction.
- Edge case: aggregate with zero domain events does not insert any outbox row.
- Error path: `TenancyInterceptor` throws when `IRequestContext` is unavailable (no ambient HTTP context), preventing accidental cross-tenant access at boundary leak points.
- Integration: a controller method that does not propagate `IRequestContext` is flagged by `MissingCorrelationAnalyzer` (ShopFlow0002, severity Warning).
- Integration: a method calling `_db.Orders.Where(...)` directly (bypassing the tenant-scoped repository) is flagged by `RawDbSetAnalyzer` (ShopFlow0001, severity Warning).
- Integration: a webhook handler missing the `[Idempotent]` attribute is flagged by `MissingIdempotentAnalyzer` (ShopFlow0003, severity Warning).
- Integration: any use of `DateTime.Now` is flagged by `DateTimeNowAnalyzer` (ShopFlow0004, severity Warning).

**Verification:**
- Package builds with no warnings of its own.
- All tests in `ShopFlow.SharedKernel.UnitTests` pass.
- A consumer project that references the package can call `services.AddShopFlowDefaults(config)` and resolve all registered services.
- All four analyzers fire on canonical violation snippets and stay silent on canonical-compliant snippets.

---

- U6. **Inventory blessed reference module (full Clean Architecture quartet, RLS, outbox, reservation ledger schema)**

**Goal:** Ship the first `Services/Inventory/` module end-to-end as the worked example AGENTS.md points to. Domain layer carries the StockItem aggregate with reservation-ledger schema and the conditional-INSERT SQL; Application layer has CQRS handlers; Infrastructure has EF Core + RLS policies + outbox; API exposes controllers + Swagger. Reservation/sync logic is *scaffolded* (the SQL exists, the domain code exists) but the 5,000-concurrent scale gate is **Phase-1 Sprint-1's** validation, not Phase 0's.

**Requirements:** R1, R2, R3, R6

**Dependencies:** U5 (consumes the meta-package), U1 (modular-monolith stance from ADR-0002)

**Files:**
- Create: `ShopFlow.sln` (root solution; subsequent `dotnet sln add` calls in U10)
- Create: `src/Services/Inventory/ShopFlow.Inventory.Domain/StockItem.cs`, `Reservation.cs`, `ReservationStatus.cs`, `Sku.cs` (value object), `Quantity.cs`, `StockAdjustmentReason.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Domain/Events/StockChangedEvent.cs`, `StockReservedEvent.cs`, `StockReleasedEvent.cs`, `StockAdjustedEvent.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/ReserveStockCommand.cs`, `AdjustStockCommand.cs`, `Handlers/ReserveStockHandler.cs`, `AdjustStockHandler.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/GetAvailabilityQuery.cs` + handler
- Create: `src/Services/Inventory/ShopFlow.Inventory.Application/Ports/IReservationRepository.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/InventoryDbContext.cs` (EF Core)
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/EntityConfigurations/StockItemConfiguration.cs`, `ReservationConfiguration.cs`
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Migrations/20260427000001_InitialInventorySchema.cs` (EF Core migration; Tech Design §7.2 schema verbatim — `stock_items`, `reservations_ledger`, partial covering index, RLS policies)
- Create: `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Repositories/ReservationRepository.cs` (the conditional INSERT CTE from Tech Design §7.2)
- Create: `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/InventoryController.cs`, `Program.cs` (composition root invoking `services.AddShopFlowDefaults()`)
- Create: `src/Services/Inventory/AGENTS.md` (module-level deltas: domain rules specific to Inventory — reservation lifecycle, ledger invariants, RLS test recipes)
- Create: `tests/ShopFlow.Inventory.UnitTests/Domain/StockItemTests.cs`, `ReservationTests.cs`, `SkuTests.cs`
- Create: `tests/ShopFlow.Inventory.UnitTests/Application/ReserveStockHandlerTests.cs` (using stub `IReservationRepository`)
- Create: `tests/ShopFlow.Inventory.IntegrationTests/ReservationRepositoryTests.cs` (Testcontainers Postgres)
- Create: `tests/ShopFlow.Inventory.IntegrationTests/RlsPolicyTests.cs` (impersonate tenant A, query expecting 0 rows from tenant B)

**Approach:**
- Schema follows Tech Design §7.2 verbatim. Both `stock_items` and `reservations_ledger` carry `tenant_id` and have RLS policies. Partial covering index `idx_active_reservations` on `(tenant_id, sku) INCLUDE (qty) WHERE status = 'active'` is part of the initial migration.
- The conditional-INSERT CTE from Tech Design §7.2 is wrapped in `ReservationRepository.TryReserveAsync()` returning `Result<Guid>`. Idempotency is by `(tenant_id, order_id) UNIQUE` constraint.
- `ReserveStockHandler.Handle` first calls `FindByOrderIdAsync` to short-circuit on existing reservation (idempotency at the application layer), then delegates to `TryReserveAsync`.
- `Program.cs` calls `services.AddShopFlowDefaults(config)` from the meta-package; only Inventory-specific registrations are added on top (DbContext, repositories).
- Module-level `AGENTS.md` is short — 30-50 lines max, only the deltas from root canon (e.g., "reservation lifecycle is Active → (Confirmed | Expired | Released); never delete reservations, only state-transition them").
- The 5,000-concurrent oversell test is **declared but not yet exercised here** — it lives in `tests/ShopFlow.PropertyTests` (U8) where it runs against a `NotImplementedException` stub for now. The Inventory module's integration tests cover the basic happy path + RLS policy enforcement.

**Execution note:** Domain methods (StockItem.AdjustStock, ConfirmDeduction) are written test-first against the unit test file. Repository SQL is implemented after the integration test naming the expected CTE behavior is red.

**Patterns to follow:** Tech Design §7.2 (reservation ledger schema + SQL) verbatim. Tech Design §7.5 (confirmation and deduction transaction shape) verbatim. Ardalis CleanArchitecture v11 layout (without the FastEndpoints / MediatR pipeline choices that conflict with the meta-package's defaults).

**Test scenarios:**
- Happy path (unit): `StockItem.AdjustStock(+10, Reason.Receiving, userId)` raises `StockAdjustedEvent` with delta=+10.
- Happy path (unit): `Reservation.Active` constructed within last 15 minutes is `IsActive == true`; constructed > 15 minutes ago is `IsActive == false` (expiry boundary).
- Edge case (unit): `StockItem.AdjustStock(-1000)` on a stock item with `TotalQuantity = 50` clamps to 0, never goes negative (per `Math.Max(0, …)` in domain code).
- Edge case (unit): `Sku` value object rejects null, empty, or whitespace-only strings.
- Error path (unit): `ReserveStockHandler.Handle` returns `Result<Guid>.Failure("oversold")` when `TryReserveAsync` returns Failure.
- Happy path (integration, Testcontainers): `TryReserveAsync` for available stock returns Success(Guid), inserts a row in `reservations_ledger` with status='active'.
- Edge case (integration): two concurrent `TryReserveAsync` calls on the same SKU with combined qty > available → exactly one succeeds, exactly one fails. (Lightweight version of the W3 5,000-concurrent gate.)
- Edge case (integration): re-calling `TryReserveAsync` with the same `(tenant_id, order_id)` returns the existing reservation (UNIQUE constraint short-circuits).
- Integration (RLS): `Covers F4 / AE3.` Connection impersonating tenant A queries `reservations_ledger` directly via raw SQL → returns only tenant A's rows even when tenant B has rows for the same SKU.
- Integration: stock adjustment raises `StockAdjustedEvent` and writes a row to `outbox_messages` in the same transaction; if the adjustment is rolled back, no outbox row exists.

**Verification:**
- Module builds with zero warnings.
- All Inventory unit and integration tests pass.
- `dotnet ef database update` from a fresh Postgres applies the initial migration cleanly; `\dt` shows `stock_items`, `reservations_ledger`, `outbox_messages`, partition tables, and RLS policies are listed via `\d+ reservations_ledger`.
- Inventory API responds 200 to `GET /healthz`; `POST /api/inventory/reservations` happy path reserves stock and returns the reservation Guid.
- Module-level AGENTS.md exists and stays under 50 lines.

---

- U7. **Mock-channel server v1 (Shopee + Lazada with HMAC + 5 named failure scenarios + control plane)**

**Goal:** Forward-deploy the mock-channel server in the dev orchestrator before Inventory's API talks to anything channel-related. Provide HMAC-signed webhooks, configurable failure injection via HTTP control plane, deterministic webhook replay, and 5 named YAML scenarios drawn from the real-fixture seed (U3).

**Requirements:** R4

**Dependencies:** U3 (real-fixture seed informs realistic payload shapes), U4 (Taskfile orchestrates startup)

**Files:**
- Create: `infrastructure/mock-channels/shopee-mock/Dockerfile`
- Create: `infrastructure/mock-channels/shopee-mock/package.json`, `index.js` (Express server)
- Create: `infrastructure/mock-channels/shopee-mock/scenarios/429-with-weird-retry-after.yml`
- Create: `infrastructure/mock-channels/shopee-mock/scenarios/webhook-redelivered-after-200-ack.yml`
- Create: `infrastructure/mock-channels/shopee-mock/scenarios/signature-clock-skew-3min.yml`
- Create: `infrastructure/mock-channels/shopee-mock/scenarios/partial-body-then-eof.yml`
- Create: `infrastructure/mock-channels/shopee-mock/scenarios/5xx-burst-30s.yml`
- Create: `infrastructure/mock-channels/shopee-mock/README.md` (control plane API spec, scenario format)
- Create: `infrastructure/mock-channels/lazada-mock/` (same shape; only the wire format and HMAC algorithm differ per Lazada docs)

**Approach:**
- Express + Node 22-alpine; tiny dependency footprint (express, ajv for scenario YAML validation, js-yaml).
- Endpoints: happy-path order/stock APIs that match the real Shopee/Lazada wire format using the U3 fixtures as response shapes; webhook signing endpoint that delivers events to a configurable target URL; control-plane endpoints (`POST /control/scenario/{name}/start`, `POST /control/scenario/stop`, `GET /control/state`) that toggle named YAML scenarios.
- Scenario YAML format: `{ name, description, behavior: { responses: [{ matchPath, returnStatus, returnBody, returnHeaders, repeat }], webhookDeliveryRules: [{ trigger, deliveryCount, signatureMode }] }`.
- HMAC verification helpers exposed for the receiving side (so Inventory's webhook receiver can re-use the same verification logic via a small npm-published-as-private or embedded copy).

**Patterns to follow:** Real Shopee/Lazada docs for endpoint paths, header names, signing algorithms. The U3 fixtures are the canonical payload shapes.

**Test scenarios:**
- Happy path: `POST /api/v1/orders` with valid payload returns 201 + an order ID matching the documented Shopee shape.
- Happy path: webhook delivery to a registered target URL fires once per state transition by default; signature header is HMAC-SHA256 of body using the configured secret.
- Edge case (control plane): `POST /control/scenario/429-with-weird-retry-after/start` followed by `POST /api/v1/orders` returns 429 with a `Retry-After` header value chosen from the scenario YAML (e.g., `Retry-After: garbage` to test parser robustness).
- Edge case (control plane): `webhook-redelivered-after-200-ack` scenario delivers the same webhook payload twice with a 5-second gap, even after a 200 ACK on the first delivery.
- Edge case (control plane): `signature-clock-skew-3min` scenario signs requests with a timestamp 3 minutes in the future, exercising the receiver's allowed-skew window.
- Edge case (control plane): `partial-body-then-eof` scenario returns the first half of a JSON response then closes the connection.
- Edge case (control plane): `5xx-burst-30s` scenario returns 503 to all requests for 30 seconds then resumes normal behavior.
- Integration: with the mock running in the dev orchestrator, an Inventory webhook receiver test (added in U10's Channel module skeleton) confirms a duplicate delivery results in exactly one persisted webhook event (validates the persistent-idempotency design from Tech Design §9).

**Verification:**
- `task up` (or `aspire run`, depending on ADR-0001) starts the mocks alongside the rest of the stack.
- `curl http://localhost:7001/healthz` returns 200.
- `curl -X POST http://localhost:7001/control/scenario/429-with-weird-retry-after/start` activates the scenario; subsequent calls return 429.
- All 5 scenario YAMLs validate against the schema and produce the documented behavior when activated.
- Mock README.md documents the control-plane API completely enough that another developer could write a test client.

---

- U8. **Test-first harnesses (FsCheck reservation-ledger property suite + k6/NBomber stock-sync load harness, against `NotImplementedException` stubs)**

**Goal:** Codify the two highest-risk components' invariants as automated tests against stub implementations that throw. The harnesses serve as the spec; when Phase-1 Sprint-1 (W3) implements the ledger and Phase-2 Sprint-5 (W7) implements the sync engine, the implementations bring the tests from red to green. Assertions are quoted directly from `02-technical-design-document.md.docx` and `01-product-development-plan.md.docx`.

**Requirements:** R5

**Dependencies:** U5 (consumes meta-package types like `Result<T>`), U6 (Inventory module exposes `IReservationRepository` interface; the FsCheck suite tests against the interface, not the implementation)

**Files:**
- Create: `tests/ShopFlow.PropertyTests/ShopFlow.PropertyTests.csproj` (FsCheck.Xunit + Microsoft.NET.Test.Sdk)
- Create: `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs`
- Create: `tests/ShopFlow.PropertyTests/Stubs/NotImplementedReservationRepository.cs` (returns `Result<Guid>.Failure("not implemented")` from every method)
- Create: `tests/ShopFlow.LoadTests/ShopFlow.LoadTests.csproj` (NBomber + xUnit harness)
- Create: `tests/ShopFlow.LoadTests/Scripts/stock-sync-burst.k6.js` (k6 script for stock change burst)
- Create: `tests/ShopFlow.LoadTests/Scripts/flash-sale-reserve.k6.js` (k6 script for 5,000 concurrent reservations against 1,000 units; quotes Plan §299 in script comment)
- Create: `tests/ShopFlow.LoadTests/Scripts/webhook-storm.k6.js` (1,000 req/s with 20% duplicates; quotes Plan §313)
- Create: `tests/ShopFlow.LoadTests/Stubs/NotImplementedStockSyncCoalescer.cs`, `NotImplementedRateLimiter.cs`, `NotImplementedPriorityQueue.cs`
- Create: `tests/ShopFlow.LoadTests/SyncEnginePrimitivesTests.cs` (NBomber harness driving the three primitives stubs)

**Approach:**
- The FsCheck property suite encodes the reservation-ledger invariants:
  1. Concurrent reservations summing to ≤ available always succeed (every individual call returns Success; `Result<Guid>.Success(_)` count == call count).
  2. Concurrent reservations summing to > available result in exactly `available / qty_per_request` successes (zero oversell).
  3. The same `(tenant_id, order_id)` reserved twice yields the same Guid both times (idempotency).
  4. After expiry, an active reservation transitions to expired and emits `StockReleasedEvent`.
  5. `sum(qty WHERE status = 'active')` ≤ `total_qty - allocated_qty` for every state.
- All 5 properties run against the `NotImplementedReservationRepository` stub and **fail** as red bars. Phase-1 Sprint-1 brings them to green.
- The k6/NBomber load harness encodes the three sync primitives' invariants:
  1. Coalescer: 100 stock changes for the same `(tenant, sku, channel)` within a 500ms window produce exactly 1 outbound push.
  2. Rate limiter: with bucket size = 100/s, sustained 1,000 req/s for 5s results in exactly 500 ± 5 served (5s × 100/s + initial burst), the rest blocked.
  3. Priority queue: a high-priority job enqueued behind 1,000 regular jobs is served within 100ms.
- Stubs throw `NotImplementedException`; the harness wraps each call in a try/catch that records the throw as "expected failure" so the harness itself runs green-while-stubs-fail. When Phase-2 Sprint-5 lands the implementations, the harness's secondary assertions (the actual invariants above) become live.
- CI runs the property suite on every PR (~2 min). Load harness runs on a nightly schedule, not per-PR (matches Tech Design §1597).

**Execution note:** This entire unit is the test-first investment. No implementation code is written until later phases. The harnesses ARE the spec.

**Patterns to follow:** Tech Design §1593 quotes the FsCheck property-test approach for the allocation engine. Tech Design §1591-1597 lists k6 scenarios that map directly to these scripts.

**Test scenarios:**
- Happy path (property): with `total_qty = 100` and 5 concurrent reservations of qty=10 each, all 5 reservations succeed and sum-active = 50.
- Edge case (property): with `total_qty = 10` and 100 concurrent reservations of qty=1 each, exactly 10 succeed and 90 fail with `"oversold"`. Zero oversell.
- Edge case (property): 1,000 idempotency-key-reused reservations with the same `(tenant_id, order_id)` produce 1 unique Guid.
- Edge case (property): expired reservations release their qty back to available; `sum(active.qty) + sum(confirmed.qty) ≤ total_qty - allocated_qty` invariant holds.
- Integration (load): 100 stock changes coalesce to exactly 1 push (k6 + counter probe; runs against a stub that records every push call → expected count = 1; against the stub that throws, count = 0 and the test fails with explicit "stub not implemented" message rather than silent skip).
- Integration (load): rate limiter shapes traffic correctly under sustained burst; the test asserts the served count and explicitly logs the stub's `NotImplementedException` to make the red-bar visible.
- Integration (load): priority queue serves a high-priority job within 100ms even with 1,000 prior regular jobs (against stub: assertion fails with `NotImplementedException` recorded as "spec, not implementation").

**Verification:**
- `task test:property` runs the FsCheck suite. All 5 properties are red against the stubs. The reason for red is `NotImplementedException` from the stub — explicit, expected, documented in test output.
- `task test:load` runs the load harness manually. All assertions are red against stubs.
- CI is configured to run the property suite on every PR. The test job is **expected to fail** in W1 because the stubs throw — this failure is asserted as the gate ("failing for the right reason"). Once Phase-1 lands the real implementation, the property suite turns green automatically and the gate inverts: now the suite must pass. (Configuration choice: instead of `dotnet test --filter` excluding red tests, the property suite asserts on the *type* of failure — `NotImplementedException` is acceptable in W1; any other failure is a real bug.)

---

- U9. **CI pipeline + dev orchestrator wiring + Phase-0 scale-gate validation**

**Goal:** Wire the dev orchestrator (Aspire AppHost or docker-compose, per ADR-0001) so `task up` brings up the full Phase-0 stack: Postgres, Redis, RabbitMQ, observability (Tempo or Aspire dashboard), the Shopee + Lazada mocks, the Inventory module's API. Wire GitHub Actions CI to run build + unit + integration + property tests on every PR with a < 10 min budget.

**Requirements:** R7

**Dependencies:** U1 (ADR-0001 settles Aspire vs Compose), U5 (meta-package), U6 (Inventory), U7 (mock channels), U8 (property suite to run in CI)

**Files (Aspire branch — if ADR-0001 = Aspire):**
- Create: `src/AppHost/ShopFlow.AppHost/ShopFlow.AppHost.csproj`
- Create: `src/AppHost/ShopFlow.AppHost/Program.cs` (registers all resources)

**Files (Compose branch — if ADR-0001 = Compose):**
- Create: `infrastructure/docker-compose.yml`
- Create: `infrastructure/docker-compose.override.yml`

**Files (both branches):**
- Create: `.github/workflows/ci.yml` (build + unit + integration + property suite + analyzer)
- Create: `.github/workflows/chaos-nightly.yml` (stub now; populated when chaos tests land in Phase 4)
- Modify: `Taskfile.yml` (add `task up`, `task down`, `task ci`, `task scale-gate`)
- Modify: `Taskfile.yml` cold-start time check task

**Approach:**
- Per Tech Design §18.1 the Compose stack includes `postgres:16`, `redis:7-alpine`, `rabbitmq:3-management`, `seq` (logs), `prometheus`, `grafana/tempo`, `minio`, `shopee-mock`, `lazada-mock`, plus Inventory API. AppHost equivalent registers each as an Aspire resource.
- CI workflow:
  1. `dotnet restore` + cache.
  2. `dotnet build` with analyzer in Warning mode.
  3. `dotnet test --filter Category=Unit` (Inventory + SharedKernel unit tests).
  4. `dotnet test --filter Category=Integration` (Testcontainers Postgres tests).
  5. `dotnet test --filter Category=Property` (FsCheck suite — asserts red-for-the-right-reason against stubs in W1; flips to must-pass once stubs are replaced).
  6. Cold-start gate: `task up` with timeout 90s; fail if exceeds.
  7. `dotnet csharpier --check .` (enforce formatter).
- Total CI budget: < 10 min on standard GitHub-hosted runner.
- `task scale-gate` runs the cold-start + auth p99 + CI total time checks locally so the developer can self-verify before pushing.

**Patterns to follow:** Tech Design §18.1 Compose stack composition. dotnet/eShop Aspire AppHost shape (if Aspire wins). MassTransit Testcontainers integration test conventions.

**Test scenarios:**
- Happy path (CI): a PR with no warnings and all tests passing-or-red-for-right-reason completes the workflow in < 10 min.
- Happy path (cold-start gate): `task up` from cold (no cached images) completes in < 90s on developer laptop with .NET 8 SDK + Docker Desktop pre-installed.
- Happy path (auth): `POST /api/auth/login` happy path returns 200 in < 150ms p99 over 100 sequential calls (lightweight perf check — not a load test).
- Edge case: a PR with a CSharpier formatting violation fails CI on the formatter step.
- Edge case: a PR introducing a `DateTime.Now` use produces a Warning in the build log (analyzer ShopFlow0004); does not yet fail the build (Warning mode in W1 per U5).
- Integration: a PR that introduces a real bug in the Inventory unit tests fails the unit test step and blocks merge.

**Verification:**
- A test PR with a trivial change (e.g., README typo) passes CI in < 10 min.
- `task up` cold-start measured at < 90s.
- Inventory `POST /api/auth/login` happy path measured at p99 < 150ms over 100 calls.
- All required status checks (build, unit, integration, property, formatter, cold-start) listed under "branch protection" recommendations in the README.

---

### Phase C — Week 2 (replicate × 5, harden, sign off)

- U10. **Replicate Inventory module shape into Inbound, Outbound, Channel, Analytics, Gateway**

**Goal:** Copy-paste the Inventory module skeleton into the 5 remaining bounded contexts and rename. Each replicated module is structurally identical to Inventory but with empty domain (placeholder aggregates and events that compile). Domain implementation for each module is the responsibility of Phase-1+ sprints; Phase 0 only ships the skeletons so the meta-package's wiring is exercised across all 6 modules.

**Requirements:** R2 (every module wires through `services.AddShopFlowDefaults()`), R7 (module skeletons must not regress cold-start gate)

**Dependencies:** U6 (Inventory blessed reference), U9 (CI validates the replicated modules)

**Files:**
- Create: `src/Services/Inbound/ShopFlow.Inbound.{Domain,Application,Infrastructure,Api}/*.csproj` (mirror Inventory layout)
- Create: `src/Services/Outbound/ShopFlow.Outbound.{Domain,Application,Infrastructure,Api}/*.csproj`
- Create: `src/Services/Channel/ShopFlow.Channel.{Domain,Application,Infrastructure,Api}/*.csproj`
- Create: `src/Services/Analytics/ShopFlow.Analytics.{Application,Infrastructure,Api}/*.csproj` (no Domain layer per Tech Design §5)
- Create: `src/ApiGateway/ShopFlow.Gateway/ShopFlow.Gateway.csproj` (YARP gateway with auth + rate limit middleware)
- Create: per-module `AGENTS.md` stubs (placeholder content: "domain coming in Phase 1 Sprint X")
- Modify: `ShopFlow.sln` (`dotnet sln add` for every new csproj)
- Modify: `src/AppHost/ShopFlow.AppHost/Program.cs` OR `infrastructure/docker-compose.yml` (register all 5 new modules as resources/services)
- Test: per-module unit and integration test projects with smoke tests (does the API start, does `/healthz` return 200)

**Approach:**
- This is **deliberately** copy-paste-rename, not a generator. Per ideation #5 anti-promoting F2.2: at N=6 the generator's authoring cost never breaks even.
- Each module's `Program.cs` is identical to Inventory's modulo namespace. The only meaningful per-module differences are placeholder DbContext + an empty `Configure` for module-specific endpoints.
- Each module's per-service `AGENTS.md` is a 5-10 line stub explicitly noting "domain coming in Phase 1 Sprint X" so AI helpers do not pattern-match to nonexistent code.
- ApiGateway uses YARP per Tech Design ADR-09. Phase 0 routes all `/api/{module}/*` to the corresponding module API; deeper routing rules wait for actual endpoints.

**Patterns to follow:** U6 Inventory module structure verbatim. YARP routing config from `Microsoft.ReverseProxy` docs.

**Test scenarios:**
- Happy path: each module's API starts via `dotnet run` and responds 200 to `/healthz`.
- Happy path: each module's `Program.cs` resolves all services registered by `services.AddShopFlowDefaults()` without error.
- Integration: cold-start gate from U9 (< 90s) still passes with all 6 modules + Gateway running.
- Integration: ApiGateway routes `/api/inventory/healthz` through to Inventory module's `/healthz` and returns 200.

**Verification:**
- `dotnet build` succeeds for the entire solution with zero warnings (analyzer still in Warning mode).
- All 6 modules + Gateway start successfully under `task up`.
- Cold-start time has not regressed past the 90s gate.
- Each module's `/healthz` is reachable through the ApiGateway.

---

- U11. **Promote Roslyn analyzer Warning → Error + ship `shopflow-gate` CLI v1**

**Goal:** Lock the canon by promoting the four analyzers from Warning to Error severity (the W2 step from ideation #6) and ship the `shopflow-gate` CLI v1 that runs Phase-N's load profile + chaos injection + post-condition assertions on every PR. Phase-0 has only the cold-start, auth-p99, and CI-time gates; the CLI is structured so Phase 1+ gates plug in.

**Requirements:** R3 (compile-time enforcement at Error severity), R7 (Phase-0 scale gate codified as a CLI invocation)

**Dependencies:** U10 (all 6 modules + Gateway must build clean at Warning before promoting to Error)

**Files:**
- Create: `tools/shopflow-gate/ShopFlow.Gate.csproj` (.NET console app, single-file publish target)
- Create: `tools/shopflow-gate/Program.cs` (CLI entry: `shopflow-gate <phase>`)
- Create: `tools/shopflow-gate/Phases/PhaseZeroGate.cs` (cold-start + auth p99 + CI-time gates)
- Create: `tools/shopflow-gate/Phases/IPhaseGate.cs` (interface for future Phase 1+ gates)
- Create: `tools/shopflow-gate/Chaos/MockChannelControlPlaneClient.cs` (injects scenarios via U7's control plane)
- Create: `tools/shopflow-gate/README.md`
- Modify: `src/Shared/ShopFlow.SharedKernel/Analyzers/ShopFlowAnalyzers.csproj` — bump default severity for ShopFlow0001-0004 from Warning to Error in the bundled `.editorconfig`
- Modify: `Taskfile.yml` — add `task gate -- <phase>` that wraps `shopflow-gate <phase>`
- Modify: `.github/workflows/ci.yml` — add a step that runs `shopflow-gate 0` after the test stages

**Approach:**
- The analyzer promotion is one .editorconfig line per rule (`dotnet_diagnostic.ShopFlow0001.severity = error`). Module-level overrides are still possible via per-module `.editorconfig` if a real exception emerges.
- `shopflow-gate` is a thin orchestrator: it loads the named phase's `IPhaseGate` implementation, spins up Toxiproxy / mock-channel scenarios, invokes the load test scripts, parses results, asserts post-conditions (cold-start time, auth p99, sample reservation invariants).
- For Phase 0 specifically, the gate runs:
  1. `task up` with timeout 90s → measure cold-start.
  2. 100 sequential `POST /api/auth/login` calls → measure auth p99 (gate: < 150ms).
  3. CI total time recorded from the previous CI run via the GitHub API → gate: < 10 min (skipped when run locally).
- Future phases plug in by adding new `IPhaseGate` implementations referencing additional load scripts and scenarios. The CLI's surface (`shopflow-gate <N>`) stays stable.

**Patterns to follow:** dotnet console app conventions. `System.CommandLine` for the CLI parser. Toxiproxy client patterns for chaos injection (referenced in Tech Design §1602; full Toxiproxy adoption is Phase 4 — for Phase 0, only the mock-channel control plane is integrated).

**Test scenarios:**
- Happy path: `shopflow-gate 0` after a clean `task up` runs all three gates and exits 0.
- Edge case: introducing a deliberate 5s delay in `Program.cs` on Inventory module's startup pushes cold-start over 90s — the gate exits 1 with an error message naming which gate failed.
- Edge case: a deliberate 200ms `Thread.Sleep` in the auth controller pushes auth p99 over 150ms — gate exits 1.
- Integration: a PR that introduces a `DateTime.Now` use **fails the build at Error severity** (post-promotion, ShopFlow0004 is now Error, not Warning).
- Integration: `shopflow-gate 0` invoked in CI succeeds on a clean PR; reports timing percentiles to the PR check summary.

**Verification:**
- `dotnet build` after promotion still succeeds with zero errors (i.e., no canon violations slipped in during W1).
- A test PR introducing `DateTime.Now` fails the build with `error ShopFlow0004`.
- `shopflow-gate 0` runs locally and in CI, exiting 0 on a clean main, exiting 1 on a regression.

---

- U12. **Phase-0 sign-off: validate all gates + final commit + tag v0.1.0-phase-0**

**Goal:** Validate that Phase-0 is genuinely complete by running every gate, verifying the deliverables list against Plan §286-293, and tagging the repo `v0.1.0-phase-0` so subsequent phases have a clean starting point.

**Requirements:** R1, R7, R8

**Dependencies:** U1-U11 all complete

**Files:**
- Modify: `README.md` — update "Current stage" section to "Phase 0 complete; entering Phase 1 Sprint 1 (Inventory)" with date and tag.
- Modify: `CLAUDE.md` — update "Recommended next steps" section to point at Phase-1 plan when it exists.
- Create: `docs/phase-gates/2026-MM-DD-phase-0-signoff.md` — capture the actual measured numbers (cold-start, auth p99, CI time, test counts) as a sign-off artifact.

**Approach:**
- Run `task scale-gate` → verifies cold-start, auth p99, CI time all pass.
- Run `dotnet test` → all unit + integration tests pass; property suite still red-for-right-reason against stubs.
- Run `shopflow-gate 0` → exits 0.
- Run `dotnet build` → zero errors at Error severity.
- Manual verification: `task setup && task up && open http://localhost:5000/api/inventory/healthz` from a fresh clone in < 5 minutes (this is Plan §343's "stranger clones the repo" gate, but at the Phase-0 level: the dashboard equivalent is the API healthz endpoint since the frontend is not yet built).
- Commit the sign-off artifact and tag.

**Patterns to follow:** Plan §400 Definition of Done per story; this is the Definition of Done for Phase-0 as a whole.

**Test scenarios:** Test expectation: none — verification is human-driven sign-off against the gate criteria. The "tests" here are the gate runs themselves; passing them is the verification.

**Verification:**
- `docs/phase-gates/2026-MM-DD-phase-0-signoff.md` exists and records measured numbers.
- Tag `v0.1.0-phase-0` exists on `main` and is pushed to origin.
- README.md and CLAUDE.md reflect the new stage.

---

## System-Wide Impact

- **Interaction graph:** The Phase-0 system is a single-process modular monolith. All inter-module communication is in-process MediatR. Domain events flow via the in-process MassTransit transport into the outbox table, then are published by the polling dispatcher (Mode A from Tech Design §11.3). At W6 split, the only changes are (a) each module's Api project becomes its own host, and (b) MassTransit transport flips from in-memory to RabbitMQ. The W6 split risks new race conditions that did not exist in-process — ADR-0002 commits to a cross-process correctness regression test as the W6 gate.
- **Error propagation:** All failures use `Result<T>` at the application boundary. Domain logic does not throw for expected failure modes (oversold, idempotency-key-reused). Unhandled exceptions surface as 500 responses with a problem-details body. The Roslyn analyzer enforces no `DateTime.Now` to prevent timing-related test flakiness.
- **State lifecycle risks:** The reservation ledger's expiry worker is **not yet running** in Phase 0 (it is part of Phase-1 Sprint-1 Inventory implementation). U6 ships the schema and the conditional-INSERT SQL; expiry is a Phase-1 concern. The property tests in U8 cover expiry semantics so the harness is ready when implementation lands.
- **API surface parity:** Once U10 lands the 5 module skeletons + Gateway, every module's `/healthz` endpoint returns the same shape (`{ status: "ok", service: "<name>", version: "<sha>" }`). Future API endpoints inherit problem-details middleware via `services.AddShopFlowDefaults()`.
- **Integration coverage:** The dev orchestrator (U9) is the integration substrate. Every integration test runs against real Postgres + real RabbitMQ via Testcontainers. Mock-channel scenarios (U7) are integrated via the control plane.
- **Unchanged invariants:** The .docx source documents and the CLAUDE.md, AGENTS.md, ADR doctrine are immutable except via explicit ADR supersession. The plan does not change `.gitignore`, `.gitattributes`, or `tools/extract-docs.{sh,ps1}` (those were established in the initial commit).

---

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| ADR-0001 picks Aspire but Aspire's Compose-output story breaks production deployment plan | Med | Med | ADR-0001 explicitly engages with this risk; the fallback is to retain a hand-maintained `infrastructure/docker-compose.yml` for production while using AppHost only for dev. Documented in ADR. |
| Roslyn analyzer false-positives during Warning mode in W1 generate noise that erodes the canon's credibility | Med | Med | U5 ships analyzers with extensive tests covering both positive (canon-violating) and negative (canon-compliant) inputs. Promotion to Error in U11 is gated on 7 days of zero false positives. Per-module `.editorconfig` overrides remain available for genuine exceptions. |
| Mock-channel server complexity in U7 grows beyond a 2-day budget | Med | Low | U7 ships only 5 named scenarios + happy-path + HMAC. Additional scenarios are deferred to Phase 2-3 as load testing surfaces real failure modes. The control plane API is the stable contract; scenarios are additive. |
| FsCheck property tests in U8 produce flaky results due to non-deterministic stub behavior | Low | Med | Stubs are explicitly deterministic (`NotImplementedException` always thrown). Properties assert on the *type* of failure, not on specific values. Random seeds are fixed in the property attributes. |
| Cold-start gate (< 90s) is unachievable with 6 module skeletons + 7 infra services + 2 mock servers | Med | High | Compose profiles split startup: `infra` (Postgres/Redis/RabbitMQ/Tempo), `mocks` (Shopee+Lazada), `services` (the 6 modules + Gateway). `task up` runs all profiles; individual workflows can scope to one profile. If 90s is genuinely unachievable, ADR-0001's reconsideration is triggered (Aspire often starts faster than Compose due to in-process orchestration). |
| Solo developer burnout if W0+W1+W2 ambition exceeds capacity | Med | High | The plan is conservative on per-unit scope: U6 (the largest unit) explicitly defers the 5,000-concurrent reservation gate to Phase 1 Sprint 1 (W3). Phase-0 ships skeletons + meta-package + the failure spec, not full ledger semantics. If even this is too much, U10 (replicate × 5) can slip a week without affecting U6/U7/U8/U9 (the substantive Phase-0 deliverables). |
| AGENTS.md (U2) blows past the ~180-instruction budget when codifying every rule | Med | Low | U2 verification step explicitly bounds line count. Rules that don't fit go into `RULES.md` as a companion file (mentioned as optional in ideation #4). |
| Real-fixture spike (U3) finds no public examples for either Shopee or Lazada | Low | Low | U3 fallback is documented: generate synthetic-but-plausible payloads matching the documented schema, clearly mark as synthetic in the README. Catalog stays seedable either way. |
| The Inventory module (U6) integration tests are flaky against Testcontainers Postgres | Low | Med | Tech Design §1587-1589 names this risk; mitigation is `IAsyncLifetime` on `WebApplicationFactory`, Collection Fixtures (not Class), pinned Postgres image tag. U5/U6 follow these conventions. |

---

## Documentation / Operational Notes

- **README.md** (U4) is intentionally a stub — minimum viable for a public repo's front door. Phase 4 (W11-12) authors the marketing-grade README with screenshots, demo video, and the "what would change for 10K tenants?" paragraph (Plan §343).
- **AGENTS.md** (U2) is the living rule canon. Treat reviewer comments on AI-assisted PRs as missing rules; update AGENTS.md weekly during sprint retros.
- **ADR-0001 and ADR-0002** (U1) are the first two of an ongoing log. Subsequent ADRs land as architectural choices crystallize. Numbering is sequential, never reused.
- **No production rollout in Phase 0.** All work happens locally and in CI. Production deployment is part of Phase 4 (W11-12).
- **Monitoring:** the Aspire dashboard (or Tempo + Prometheus + Grafana under Compose) renders OTel traces, metrics, and Seq logs from W1 onward. Every business event in the Inventory module emits a span; the analyzer enforces correlation propagation.
- **Backups:** N/A in Phase 0 (no production data exists). Tech Design §14.3 names quarterly restore drills as a Phase 4 concern.

---

## Sources & References

- **Origin document:** [docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md](../ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md)
- **Product specification:** [01-product-development-plan.md.docx](../../01-product-development-plan.md.docx) (extracted text in `docs/source/01-product-development-plan.md.txt`)
- **Technical design:** [02-technical-design-document.md.docx](../../02-technical-design-document.md.docx) (extracted text in `docs/source/02-technical-design-document.md.txt`)
- **Project context:** [CLAUDE.md](../../CLAUDE.md)
- **GitHub repo:** github.com/longuit2002-blip/shopflow-wms
- **External — Aspire 13:** github.com/microsoft/aspire/discussions/10644 (roadmap)
- **External — eShop reference:** github.com/dotnet/eShop
- **External — MassTransit Sample-Outbox:** github.com/MassTransit/Sample-Outbox
- **External — Ardalis CleanArchitecture:** github.com/ardalis/CleanArchitecture
- **External — AGENTS.md convention:** deployhq.com/blog/ai-coding-config-files-guide, humanlayer.dev/blog/writing-a-good-claude-md, builder.io/blog/agents-md
- **External — Husky.NET + CSharpier:** medium.com (`@teransarathchandra/bulletproof-your-net-commits-with-husky-and-csharpier-140f5698c344`)
- **External — Taskfile:** taskfile.dev
- **External — Testcontainers .NET:** milanjovanovic.tech/blog/testcontainers-best-practices-dotnet-integration-testing
