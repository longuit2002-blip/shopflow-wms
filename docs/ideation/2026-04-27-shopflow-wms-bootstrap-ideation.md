---
date: 2026-04-27
topic: shopflow-wms-bootstrap-from-scratch
focus: setup this source from scratch given 01-product-development-plan and 02-technical-design-document
mode: elsewhere-software
run_id: 0bf0848b
---

# Ideation: Bootstrapping ShopFlow WMS Source From Scratch

## Topic Context

**ShopFlow WMS** — 12-week single-developer portfolio Warehouse Management System for SEA marketplaces (Shopee, Lazada, TikTok Shop, Shopify). 6 microservices in C# .NET 8 + Next.js 14 + Postgres/Redis/RabbitMQ + MassTransit (sagas, outbox) + OpenTelemetry + Docker Compose dev. Append-only reservation ledger; stock-sync coalescing + per-channel rate limit + priority queue; persistent webhook idempotency via Postgres `UNIQUE(channel_id, provider_event_id)`; multi-tenant RLS from day 1; mocked Shopee/Lazada servers. 5 phases (Foundation W1-2 → Core WMS W3-5 → Multi-Channel + Sync W6-8 → Real-time + Analytics W9-10 → Harden + Ship W11-12), each with hard scale gates.

**Hard non-functional constraints**: correctness over latency; idempotency everywhere; multi-tenancy from day 1 even at MVP single-tenant; observability built in Phase 0 not retrofitted; no cloud lock-in.

**Stated risks**: scope creep (high/high), sync-engine complexity (high/medium), MassTransit-saga learning curve, distributed-systems debugging eats sprint time, reviewer-skim risk, six-services-for-one-dev cost (acknowledged tradeoff in Tech Design §6).

**External grounding**: `dotnet/eShop` (.NET 9 + Aspire, no MassTransit) is the closest Microsoft reference but architecturally different; Ardalis CleanArchitecture v11 is single-service only; Mehmet Ozkaya's `aspnetrun-microservices` is the closest public analog stack-wise. MassTransit Sample-Outbox shows the canonical EF outbox wiring. Aspire 13 GA (2026) replaces docker-compose for local dev with `aspire run`; Compose-output for deployment is "in progress." AGENTS.md is the cross-tool standard for AI-agent rules with a ~150-200 instruction budget. Husky.NET + CSharpier are the .NET pre-commit baseline; Taskfile is the cleanest .NET monorepo task runner. OpenWMS.org is the most mature OSS WMS bounded-context model (Java/Spring).

---

## Ranked Ideas

### 1. Bootstrap as a modular monolith; mechanically split at the W6-8 channel-integration seam

**Description:** Reject the "6 services from W1" reading of the design. Stand up ONE .NET solution with six logical modules (separate `.csproj` per bounded context: Inventory, Inbound, Outbound, Channel, Analytics, Gateway) running in a single host with in-memory MediatR — but already with `tenant_id`/RLS/outbox semantics and the same Clean Architecture layering the design specifies. Plan the *mechanical* split at W6 when the channel adapter framework and stock sync engine arrive: that is when async cross-process messaging actually pays its own freight. Phase 0 ships ONE container; Phase 2 splits.

**Warrant:** `direct:` Tech Design §6 explicitly says "A reviewer is right to ask: does a 12-week portfolio project earn six microservices? The answer is that it doesn't, and we know it. ... Internally each service is structured as a modular monolith." Plan §10 names "scope creep (High/High)" and "Distributed systems debugging eats sprint time (Medium/High)" as the two top risks. `external:` Sam Newman's *Monolith to Microservices*; modular-monolith-with-vertical-slice is the consensus 2024-2025 recommendation for solo builds.

**Rationale:** Splitting at W6 lets you walk a reviewer through "here is when async messaging earned its keep, here is the diff that did the split, here is the latency before/after" — concrete senior-engineering evidence rather than week-1 ceremony.

**Downsides:** Reviewer who skims for "microservices" pattern-matching may bounce before W6 if README does not lead with the eventual topology. Mitigation: README opens with the final 6-service diagram and labels Phase 0-1 as "modular monolith stage." The W6 split flips MassTransit transport from in-memory to RabbitMQ, which may surface race conditions that did not exist in-process — make the split itself a scale-gate event with a load test attached.

**Confidence:** 78% · **Complexity:** Medium · **Status:** Unexplored

---

### 2. Write ADR-001 in W0 deciding Aspire vs Compose, with Aspire AppHost + seeded fixture as the candidate to beat

**Description:** Aspire 13 is GA in 2026 and the closest Microsoft reference (`dotnet/eShop`) uses it. Make Aspire-vs-Compose the **first** ADR, written before any service code. Treat Aspire AppHost orchestrating Postgres/Redis/RabbitMQ/Tempo/mock-channels + 6 modules + a deterministic seed fixture (10 SKUs, 3 tenants, 50 orders, 2 channels) as the candidate to beat: F5 puts the dev at a coherent system in <60s with traces in the Aspire dashboard. The ADR concludes either "Aspire wins; Phase 0 spec changes accordingly" or "Compose wins, here is why, and the answer is on file." If Compose wins, encode the Phase-0 cold-start gate (`docker compose up --wait` < 90s) as a CI check on PR #1 so the gate self-enforces.

**Warrant:** `external:` Aspire 13 GA in 2025-2026 "replaces docker-compose for the local dev run loop with `aspire run`" with DNS-based service discovery, dashboard with OTel traces/logs/metrics. `direct:` Plan §165 + ADR-10 commit to "no cloud lock-in" and "Docker Compose for dev." Plan §8.2 names Phase-0 cold-start < 90s as the scale gate.

**Rationale:** A 2026 reviewer will ask "why not Aspire?" — having a written answer is the difference between "principled choice" and "didn't track the ecosystem." The seeded-fixture loop is the leverage move: cheap iteration → faster convergence on the hard sync-engine bugs in Phase 2-3, regardless of which orchestrator wins.

**Downsides:** Aspire's Compose-output-for-deployment story is "in progress" (not first-class), which conflicts with the no-cloud-lock-in constraint if naively adopted. The ADR has to engage with that tension, not paper over it.

**Confidence:** 82% · **Complexity:** Low (the ADR); Medium (Aspire adoption) · **Status:** Unexplored

---

### 3. Build the mock-channel server BEFORE service #1, as an adversarial flight-simulator catalog of marketplace pathologies

**Description:** Treat the Shopee/Lazada mocks not as test scaffolding but as the **flagship engineering artifact** of the project — a curated, versioned catalog of marketplace failure modes with HMAC verification, configurable 429/5xx/latency injection via HTTP control plane, deterministic webhook replay, and a recorded-fixture library. Build it BEFORE service #1 in Phase 0. Each scenario gets a name, a YAML, and a passing integration test (`429-with-weird-retry-after.yml`, `webhook-redelivered-after-200-ack.yml`, `signature-clock-skew-3min.yml`, `partial-body-then-eof.yml`). One pre-W1 spike captures real Shopee+Lazada webhook payloads to seed the fixture library; the mocks then layer adversarial mutation (random 429s, duplicate event IDs across tenants, schema drift) on the real shape.

**Warrant:** `direct:` Plan §348: "Mock servers that reproduce the hard parts (signatures, rate limits, 429/5xx behavior, idempotency tokens) demonstrate more engineering per week and let us chaos-test failure modes that real sandboxes cannot reliably inject" — the mocks ARE the engineering. Tech Design §9 hinges webhook idempotency correctness (100%, non-negotiable) on Shopee/Lazada's "at least once" delivery semantics including "redeliver events even after a 200 response." `external:` Anchanto's production multi-channel WMS uses webhook-primary + polling-fallback + idempotent-on-marketplace-order-ID.

**Rationale:** Coalescing logic, saga compensation, oversell compensation, rate-limit handling — every later phase's scale gate depends on producing deterministic adversarial responses. Built once in Phase 0, the catalog amortizes across all four phase gates. Bonus: forcing every service to wire against the mock from W1 means no service ever ships with a hardcoded happy-path channel client. Bonus²: the catalog is the **portfolio centerpiece**.

**Downsides:** Phase 0 already has a heavy deliverable list. Mitigation: ship the mock in two passes — happy-path + HMAC + 5 failure modes by end of W1, full catalog grows through Phases 2-3 as scenarios surface in load testing.

**Confidence:** 85% · **Complexity:** Medium · **Status:** Unexplored

---

### 4. Author AGENTS.md as executable canon: root + per-service hierarchy + a blessed reference service

**Description:** Write `AGENTS.md` at the repo root as the very first commit, capping at ~180 instructions. Codify the non-negotiable rules: `tenant_id`-on-every-table, outbox-or-don't-publish, `Result<T>` for failures, idempotency-key on every webhook handler, no `DateTime.Now`, no raw `DbSet` queries, naming conventions. Build *one* service end-to-end (Inventory) as the "blessed reference," and have the root AGENTS.md point at it ("when in doubt, copy the pattern from `src/Services/Inventory/`"). Per-service AGENTS.md stubs add only the service-specific deltas. Where rules can be made executable, ship them as Roslyn analyzers / `dotnet test` rule-pack so violations fail CI, not just code review. Treat every reviewer/Copilot suggestion that violates the canon as a missing rule, not a one-off correction. Optional companion: `RULES.md` seeded with cross-project priors that grows per incident.

**Warrant:** `external:` AGENTS.md is "the cross-tool universal standard (works as fallback for Claude Code, Codex CLI, Cursor)" with documented convention of root + per-service files and ~150-200 instruction budget; "best source of improvement is treating every code-reviewer comment on an AI-assisted PR as a missing rule." `direct:` Plan §165-388 + Tech Design §16 commit to convention discipline being built in Phase 0 not retrofitted.

**Rationale:** A solo dev relying on Copilot/Claude Code/Cursor for 12 weeks of distributed-systems work will produce inconsistent code unless the rules are encoded once and a worked example exists for the agent to anchor against. By service #4 the agent's first draft passes the analyzer + tenant-guard + outbox checks because it learned from service #1. This is the single highest-leverage move for the solo+AI workflow this project effectively requires.

**Downsides:** The 180-instruction budget is real; cramming every rule causes the agent to ignore most. Discipline is required. Roslyn analyzers cost a half-day each + maintenance.

**Confidence:** 88% · **Complexity:** Low (initial file); Medium (analyzers, ongoing curation) · **Status:** Unexplored

---

### 5. Ship an internal cross-cutting NuGet meta-package — but skip the `dotnet new` template generator

**Description:** Package the cross-cutting wiring (Serilog + OTel + W3C TraceContext, EF Core + tenancy `SaveChangesInterceptor` + outbox interceptor + outbox dispatcher, MassTransit bus config with retry/redelivery defaults, health endpoints, Swagger, problem-details middleware, `Result<T>`, `BaseEntity`, `IRequestContext`) as a single internal NuGet (`ShopFlow.SharedKernel`-style). Each service references it and gets the convention with one `services.AddShopFlowDefaults()` call. **Do NOT** wrap a `dotnet new shopflow-svc` template generator — at N=6 the generator's 2-3 day cost never breaks even against 2 hours of copy-paste-rename for the per-service composition root. The package is the upgrade path; copy-paste is the per-service skeleton. This is the "Distillery Mash Bill" pattern: ruthlessly fix every uninteresting variable so domain logic is the only thing that varies.

**Warrant:** `direct:` Tech Design §11.2 specifies the EF interceptor outbox pattern as identical across services by design; §4 specifies the tenancy interceptor pattern as identical; §16 specifies OTel propagation as identical. `reasoned:` 6 × ~8 cross-cutting concerns = 48 drift sites under copy-paste. A single versioned package gives one upgrade path; one bug fix propagates with `dotnet restore`. Generator breakeven never arrives at N=6.

**Rationale:** When an OTel attribute convention changes mid-project, you bump one package version, not edit 6 Program.cs files. When a poison-message handling bug surfaces, it's one PR, not six. The "skip the generator" half is the YAGNI discipline distinguishing this from yak-shaving.

**Downsides:** SemVer discipline + a small contract-test suite that runs against all consumers before publish. Some cross-cutting concerns (saga state machines) don't generalize cleanly and shouldn't be in the meta-package.

**Confidence:** 80% · **Complexity:** Medium · **Status:** Unexplored

---

### 6. Empty-bay schema discipline + Roslyn analyzer that fails the build on raw `DbContext` access

**Description:** From day 1, every schema and contract carries the future scale dimensions even though they are unused at MVP: RLS policies wired for N tenants at N=1; `webhook_events` carries `(channel_id, provider_event_id)` UNIQUE at M=1; saga state machines have `Compensating` branches at no-op; outbox messages carry `schema_version` at v1; OTel resource attributes carry `tenant.id` and `service.tier`. Empty bays cost ~30 min per concern at construction; retrofitting is multi-week. Crucially, ship a Roslyn analyzer (in the cross-cutting NuGet from #5) that fails the build on raw `DbSet<T>` queries that bypass the tenant-scoped repository, `IPublishEndpoint` calls without correlation-context propagation, webhook handlers without an `[Idempotent]` attribute, `DateTime.Now`. RLS at the DB layer is necessary but not sufficient — the compiler is the enforcer that scales across 6 services and 12 weeks of solo velocity.

**Warrant:** `direct:` Tech Design §4.5: "the 'tenant_id on day one' discipline ... the cheapest scale decision in the whole design: zero runtime cost, eliminates the most painful migration." §9.3 specifies the UNIQUE constraint from day 1; §11.1 specifies outbox `schema_version` from day 1. `reasoned:` Empty-bay analogy from Nimitz-class carriers — empty compartment is the artifact, installing it later is impossible.

**Rationale:** Without the analyzer, by W6 someone (you, tired, at 11pm) writes `_db.Orders.Where(o => o.Id == id)` directly and a tenant-isolation bug ships. With it, the build is red before commit. W12 value: zero cross-tenant incidents to debug, and the answer to "what changes for 10K tenants?" is genuinely "config and a migration."

**Downsides:** Roslyn analyzers have a learning curve. False positives during W1-2 will be annoying. Mitigation: ship in `Warning` mode for 1-2 sprints, promote to `Error` once the canon is stable.

**Confidence:** 82% · **Complexity:** Medium · **Status:** Unexplored

---

### 7. Test-first the reservation ledger and stock-sync engine in W1-2 against `NotImplementedException` stubs; one phase-gate CLI runs all prior gates on every PR

**Description:** In W1-2, before any service domain code, write the FsCheck property-based suite for the append-only reservation ledger (concurrent reservations, no oversell, idempotent reapply, monotonic seq, sum-of-deltas == current reservation, no negative free stock) and the k6/NBomber load harness for the three stock-sync primitives — using stub implementations that throw `NotImplementedException`. The harnesses are the spec. When W3 arrives for the ledger and W7 for sync, the dev writes against a red bar that has been live for weeks. Then build a `shopflow-gate` CLI that takes a phase number, runs that phase's load profile + chaos injection (via the mock-channel control plane and Compose `pause`), and asserts post-conditions on the ledger and outbox. Every PR runs the gate for **all prior phases** in CI.

**Warrant:** `direct:` Plan §299: "Scale gate: 5,000 concurrent reservation requests against 1,000 units of stock produce exactly 1,000 successful reservations, 4,000 explicit failures, zero oversell. p99 < 200ms" — this is a property-based assertion in plain English. Plan §316-323 specifies the three stock-sync primitives in isolation. Plan §10 names "sync-engine complexity (high impact)" with mitigation "Phase 2 starts with a spike week building the coalescing + rate-limit primitive in isolation" — a spike with a pre-existing harness is 3× more productive than a spike that builds its own scaffolding. Plan §285: "Each phase has a scale-validation gate. ... non-negotiable."

**Rationale:** The two highest-risk components both have correctness proofs that can be deferred into Phase 0 without committing to implementation choices. The phase-gate CLI converts every PR into a regression check for every prior phase — a W9 bug that breaks the W3 oversell guarantee is caught the same day, not in W11 hardening.

**Downsides:** Pre-implementation test authorship costs ~2-3 dev-days in W1-2 against an already-tight Phase 0. Mitigation: assertions are quoted directly from the design doc, so they're documenting what's already specified. CLI itself is ~2 more days; if Phase 0 is tight, defer the CLI to W3 and run tests under `dotnet test` / `k6 run` from a Taskfile entry until then.

**Confidence:** 80% · **Complexity:** Medium · **Status:** Unexplored

---

## Recommended Bootstrap Sequence

The 7 ideas above translate into a concrete sequence. **All seven are compatible** — they form a coherent stance on what week 0–2 looks like.

### Week 0 (pre-Phase-0, ~2-3 days)
- **Write `docs/adr/0001-aspire-vs-compose.md`** (idea #2). Decide explicitly. The decision sets the rest of W0-1.
- **Write `docs/adr/0002-modular-monolith-first.md`** (idea #1). State that Phase 0-1 ship one .NET solution with six logical modules; mechanical split happens in W6 when the channel adapter framework arrives. Lock the W6 split as a planned event with its own scale gate.
- **Write root `AGENTS.md`** (idea #4). ~180-instruction budget. Cover: `tenant_id`-on-every-table, outbox-or-don't-publish, `Result<T>` for failures, idempotency on every webhook, no `DateTime.Now`, no raw `DbSet`, naming, layering. Point at `src/Services/Inventory/` as the planned blessed reference (will exist by end of W1).
- **Capture real Shopee + Lazada webhook fixtures** via a 1-day integration spike (idea #3). Save under `tests/fixtures/channels/{shopee,lazada}/`. Even if you never connect to a real seller, this seeds the failure-library mock with real shapes.
- **Repo skeleton**: monorepo layout per Tech Design §5 (`src/`, `tests/`, `infrastructure/`, `docs/`), Taskfile.yml at root, `task setup` as the only documented onboarding command, Husky.NET + CSharpier wired via `task pre-commit`, `.editorconfig`, `.gitignore`, `README.md` (start with the eventual 6-service diagram + a "Phase 0-1 = modular monolith stage" note).

### Week 1 (Phase 0 begins)
- **Day 1-2: Build `ShopFlow.SharedKernel` meta-package** (idea #5). Wire: Serilog + OTel + W3C, EF interceptor for outbox + tenancy, MassTransit bus defaults, health/Swagger/problem-details, `Result<T>` / `BaseEntity` / `IRequestContext`. One `services.AddShopFlowDefaults()` call.
- **Day 1-2 (parallel): Ship the Roslyn analyzer in Warning mode** (idea #6). Rules: no raw `DbSet`, missing correlation propagation, missing `[Idempotent]`, `DateTime.Now`. Bundle in the meta-package.
- **Day 3-4: Build the blessed reference module — Inventory** (idea #4). End-to-end: domain (StockItem aggregate, reservation ledger), application (CQRS handlers), infrastructure (EF + RLS + outbox + tenancy interceptor), API (controllers + Swagger). With `tenant_id` and the empty-bay contracts from idea #6 wired (RLS at N=1, schema_version at v1, saga compensation branches stubbed).
- **Day 3-4 (parallel): Build the mock-channel server v1** (idea #3) — happy-path + HMAC + 5 failure scenarios (`429-retry-after`, `webhook-redelivered-after-200`, `signature-clock-skew-3min`, `partial-body-then-eof`, `5xx-burst-30s`). Each scenario named, YAMLed, integration-tested. Run as a long-running service in the dev orchestrator (Aspire AppHost or compose).
- **Day 5-6: Test-first harnesses** (idea #7). FsCheck property suite for the reservation ledger written against `NotImplementedException` stubs (assertions from Plan §299). k6/NBomber harness for stock-sync primitives written against stubs (assertions from Plan §316-323). Wire into CI.
- **Day 7: First scale gate** — `task ci` runs build + analyzer (Warning) + unit + Testcontainers integration + property suite + load suite. The reservation property tests fail (red bar by design). The cold-start gate (Aspire `dotnet run` < 60s, or `docker compose up --wait` < 90s) passes in CI.

### Week 2 (Phase 0 completes)
- **Day 1-2: Copy-paste the Inventory module shape into Inbound, Outbound, Channel, Analytics, Gateway** as logical modules in the same solution. Each gets its `.csproj` quartet (Domain/Application/Infrastructure/API), references the meta-package, owns its module folder under `src/Services/<Name>/`. **Skip the template generator** — at N=6 it never breaks even.
- **Day 3: Promote the Roslyn analyzer from Warning to Error.** The canon is now stable enough.
- **Day 4-5: `shopflow-gate` CLI v1** (idea #7). Takes a phase number, runs that phase's load + chaos profile, asserts post-conditions. Wire into CI to run all prior phases on every PR. At end of W2, only Phase 0 gate exists.
- **Day 6-7: Phase 0 scale gate validation** — cold-start under threshold, auth happy-path p99 < 150ms, CI < 10 min. Demo to yourself: `task setup` from clean clone + `aspire run` (or `docker compose up`) + open dashboard + see traces flowing through Inventory.

### Week 3 onwards
Proceed per the original Plan §8.3 onwards — Sprint 1 (Inventory), Sprint 2 (Inbound), Sprint 3 (Outbound), etc. — but on the modular-monolith-first foundation. The **W6 mechanical split** (idea #1) is a planned event: extract each module into its own host process, swap MassTransit transport from in-memory to RabbitMQ, run the Phase 2 scale gate to validate the split did not regress correctness.

### Open questions for Week 0 to resolve
- **Aspire vs Compose** (idea #2 ADR). Recommend trying Aspire for the dev loop with Compose retained as the deployment target — gives you the dashboard + DNS without giving up the no-cloud-lock-in commitment.
- **Real-fixture spike: which marketplace, which seller?** (idea #3). Even a single day reading Shopee's developer docs + saving sample webhook payloads from public examples is enough to seed the catalog.
- **AGENTS.md vs ADRs as the living architecture doc** (idea #4 open question). Recommend keeping ADRs for permanent decisions (form: numbered, dated, immutable, superseded-by-link) and using AGENTS.md for the running rules + worked example. Don't replace, complement.

---

## Cross-Cutting Combinations

- **#1 + #5 + #6 together** form a coherent "modular monolith with unbreakable cross-cutting discipline" stance: one deployable through W6, but every domain edge already wears the empty-bay contracts and the Roslyn-enforced canon, so the W6 mechanical split is a compose-file change, not an architecture change.
- **#3 + #7 together** make the chaos and load story honest end-to-end: the failure-library mock supplies the adversarial inputs, the property/load harness asserts the invariants survive them, and the phase-gate CLI keeps both running on every PR.
- **#2 + #4 together** front-load the two decisions a 2026 reviewer will most reliably notice: "did you evaluate Aspire?" and "do you have a serious AI-pair-programming workflow?" Each gets a written, dated answer in the repo.

---

## Rejection Summary (highlights)

| # | Rejected Idea | Reason |
|---|---|---|
| F1.7 | Taskfile `task setup` as the only README command | Tactical only; folded into bootstrap sequence W0 |
| F2.2 | `dotnet new` template generator for services 2-6 | Anti-promoted in #5 — never breaks even at N=6 |
| F3.1 | Ship the mock as a standalone OSS product first | Doubles the marketing scope; rejected as too expensive |
| F3.2 | Design-doc-first sprints with public review channel | Conflicts with 12-week build cadence |
| F3.4 | Fork OpenWMS.org and contribute SEA adapter layer | Subject-replacement (rewrites project as Java/Spring contribution) |
| F3.5 | Build it in F# or Rust | Conflicts with stated stack commitment |
| F4.6 | Saga state-chart compiled from YAML/DSL | Too expensive at ~4 sagas |
| F5.5 | Bench Trial Stipulations ADR doctrine | Form-only insight; ADRs already planned |
| F6.2 | Zero-Backend ShopFlow on Supabase + Edge Functions | Subject-replacement |
| F6.3 | The 5-Agent Orchestra (1 human + 5 LLM agents) | Too speculative; insight folded into #4 |
| F6.4 | Production-from-W1 with 3 real SEA sellers | Conflicts with portfolio framing; insight folded into #3 |
| F6.5 | Fork OpenBoxes / OpenWMS, rewrite spine in .NET 8 | Subject-replacement |
| F6.6 | The 1M-Tenant Day-One Architecture | Insight covered by #6's empty-bay discipline |
| F6.7 | No-Local-Dev: Codespaces + Aspire-on-ACA only | Conflicts with no-cloud-lock-in constraint |

Full 48-idea source list with all 6 frame outputs lives at `C:\Users\longc\AppData\Local\Temp\compound-engineering\ce-ideate\0bf0848b\raw-candidates.md`.
