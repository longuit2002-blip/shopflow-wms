---
title: Portfolio finish-line — prove the hard problems, make multi-channel real, ship demonstrable
type: feat
status: active
date: 2026-05-27
revised: 2026-05-27
origin: docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md
---

# Portfolio finish-line — prove the hard problems, make multi-channel real, ship demonstrable

> **Revision note (2026-05-27, post doc-review).** A headless doc-review plus direct source verification overturned this plan's original central premise. The plan first assumed the four hard-problem proofs were complete, Skip-for-Docker tests where you "flip the Skip and they pass." Reading the actual files showed the proof estate is **two-tier**: some proofs have real bodies on working Testcontainers fixtures (run-locally-now), but **three are empty `return Task.CompletedTask;` stubs behind a Skip** — un-skipping them yields trivial green that proves nothing. The user's decision (recorded in this session) is to **write all three real harnesses**, which reverses the original K2 ("reuse only, author no new proof coverage"). This revision re-scopes accordingly: the proof estate is reclassified honestly, three harness-writing units are added (sized LIGHT / MEDIUM / HEAVY against the scaffolding that already exists), a Lazada webhook-signature blocker is folded into the Lazada unit, and the `task up` fallback is hardened to name a concrete service floor. See the Verification Findings section for the source evidence.

## Summary

Get ShopFlow WMS from "22 green sprint tags that have never run" to "an engineer can clone it, run the proofs, and read a README that routes straight to the impressive engineering." Three halves: **(A) make-real-proofs-runnable** — get the genuinely-real proof suites green locally now that Docker is available, behind an env-gated opt-in; **(B) write the missing proofs** — three of the headline hard-problems are empty stub harnesses, so write them on top of the fixtures/drivers that already exist; and **(C) net-new build + ship** — a second channel adapter (Lazada) that makes "multi-channel" honest and demonstrates the plugin architecture, a minimal `task up` repair so the stack boots enough to explore, and a README reframed from changelog-wall to engineer-routing.

---

## Problem Frame

The first live boot (2026-05-27) proved the Aspire dev stack had never run; five first-boot breakages were found, three remain, and the README buries the genuinely hard engineering under a sprint-by-sprint changelog. Two deeper problems surfaced during planning verification:

1. **The "proofs" are partly hollow.** Of the named hard-problem proofs, two run for real (oversell scale gate, reservation-ledger FsCheck properties), one cross-cutting tenant-routing test runs for real, but **three are empty `Task.CompletedTask` stubs** (StockSync noisy-neighbor, Auth cross-tenant isolation, cross-role RBAC denial) — written to be Skip'd, with the harness bodies deferred sprint after sprint and never implemented. An evaluator who runs them after a naive "un-skip" sees green that asserts nothing.
2. **The "multi-channel marketplace WMS" headline is hollow** — only `ShopeeAdapter` exists despite a pluggable `IChannelAdapter` framework, and the webhook receiver hardcodes the Shopee signature header, so a second channel can't even receive a webhook.

For the chosen evaluator (an engineer who clones, runs, and reads), each of these is disqualifying: a broken `task up`, hollow proofs, and a one-channel "multi-channel" claim each undercut the project before its real strengths are seen.

See [docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md](../brainstorms/2026-05-27-portfolio-finish-line-requirements.md) for the full problem frame, actors, flows, and the 2026-05-27 scope amendment that folded Lazada in.

---

## Requirements

Carried from origin (see origin for full text):

**Make-existing-demonstrable**
- R1. Each of the four hard-problems has a proving test that runs green locally (Docker + Testcontainers) via a documented command — **including the three that must first be written from their empty stubs.**
- R2. The `Category=Load` scale-gate tests are confirmed to actually pass locally (never run locally before — discovery).
- R3. Each hard-problem maps explicitly: proving-test path ↔ production code path.
- R4. README reframed to engineer-orient (hard-problems routing + architecture diagram + how-to-run + how-to-run-the-proofs); changelog moved to a history doc.
- R5. README forward-looking section ("100+ tenants" / "SOC2 follow-up").
- R6. `task up` boots enough for an engineer to explore (a named minimum service floor, not all-six-services-perfect).
- R7. Accurate "run locally" doc (Docker prerequisite + native-Postgres-port coexistence reality).
- R8. Code navigable, git history coherent, no committed local-run hacks.

**Multi-channel breadth (scope amendment)**
- R9. Lazada adapter implemented end-to-end against a Lazada mock (webhook receive + signature verify + stock push + product mapping); no `IChannelAdapter` framework/factory change beyond the new adapter + its registration. **Includes making the webhook receiver's signature extraction channel-agnostic** (currently Shopee-hardcoded).
- R10. A test proves a 2nd channel flows through the receiver + the sync engine pushes stock to BOTH channels through the same coalescing/rate-limit/priority/breaker pipeline.
- R11. Lazada is one of the README-routed hard-problems — the "pluggable channel framework" claim links Shopee + Lazada adapters side by side + the proving test.

**Origin actors:** A1 (engineer/tech-interviewer — primary evaluator), A2 (maintainer).
**Origin flows:** F1 (engineer evaluation), F2 (clone-and-run-enough), F3 (run-the-proofs).
**Origin acceptance examples:** AE1-AE4 (the four proofs green), AE5 (README routing), AE6 (clone-and-run-enough), AE7 (Lazada webhook idempotent), AE8 (multi-channel push to both channels).

---

## Verification Findings (why this plan was revised)

Direct source reads on 2026-05-27 (the same run-first discipline that caught the Sprint-13 migration-ordering bug):

**Real bodies, run-locally-now (Testcontainers; Docker is now available — 29.4.2):**
- `tests/ShopFlow.Inventory.IntegrationTests/MultiTenantScaleGateTests.cs:47` — `[Fact]`, **no Skip**, full 5-tenant × 1,000-reservation body asserting zero oversell + fairness floor. Backed by `InventoryTenantFixture`.
- `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs:38` — real FsCheck `[Property]` bodies (happy-path concurrency, strict-capacity no-oversell, idempotency, expiry, invariant-for-any-sequence). `Category=Integration` (NOT `Category=Property` as the original plan claimed). Backed by `PostgresPropertyFixture`.
- `tests/ShopFlow.SharedKernel.IntegrationTests/CrossTenantRoutingTests.cs:36` — 5 real `[Fact]`, no Skip, `FakeTenantCatalog` + real DB read through the routing binding.
- (Fixtures proven by sibling happy-paths: `tests/ShopFlow.Outbound.IntegrationTests/SagaHappyPathTests.cs` + `tests/ShopFlow.StockSync.IntegrationTests/StockSyncHappyPathTests.cs` both have full real bodies on working fixtures + `TenantBurstDriver`.)

**Real body, Skip-marked, fixture-seeding unverified (light verification at run):**
- `tests/ShopFlow.Auth.IntegrationTests/Authorization/CrossTenant403Test.cs:50` — `[Fact(Skip)]` with a **real** WAF body (`_fixture.HttpClient.GetAsync("/api/auth/admin/users")` asserting never-200 cross-tenant). Depends on `AuthAdminAuthorizationFixture` provisioning tenant-A + tenant-B as two DBs — that seeding is asserted in a comment, not verified to exist. Treat as MEDIUM-LIGHT: un-skip, run, confirm the fixture actually provisions both tenants (and implement the seeding if it doesn't).

**Empty `return Task.CompletedTask;` stubs behind a Skip — MUST be written (the reversal):**
- `tests/ShopFlow.StockSync.IntegrationTests/MultiTenantStockSyncScaleGateTests.cs:59,82` — 2 empty stubs (noisy-neighbor AE2). **Scaffolding exists and is proven**: `StockSyncHappyPathTests` shows the `WebApplicationFactory<Program>` + multi-tenant catalog provisioning + `TenantBurstDriver` + `FakeChannelAdapterFactory` path works end-to-end; `FairnessCalculator.cs` exists. → **LIGHT.**
- `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` — 11+ empty stubs (cross-role denial AE4). `HandoffFixture` is a real `WebApplicationFactory<Program>` WAF with **all four role JWT builders already wired (incl. Packer)** — but its DB migrate + seed steps are **commented out** as "CI-tier body (omitted from local skipped run)" (`HandoffFixture.cs:164-186`). → **MEDIUM** (implement the 5 commented seed steps, then the denial bodies are simple 403 + saga-state-unchanged asserts).
- `tests/ShopFlow.Auth.IntegrationTests/AuthCrossTenantTests.cs:16` — 5 empty stubs (tenant isolation AE3). `AuthTenantFixture` is real but **repository-level only** ("not full request-pipeline tests" — no WAF boot). The `MultiTenantAuthFixture` named in CLAUDE.md was never built. → **HEAVY** (build a multi-tenant Auth WAF fixture, modeled on `HandoffFixture` + `AuthTenantFixture` + `CrossTenant403Test`'s WAF pattern; some R32 scenarios are near-tautological at the DB layer and can stay repo-level).

**Webhook signature blocker (SEC-U4-001):**
- `src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs:72` hardcodes `[FromHeader(Name = "X-Shopee-Signature")] string? signature`. A Lazada webhook carries a different header → `signature == null` → 401 at line 110-114 **before** the (correctly channel-agnostic) `_verifierFactory.Resolve(binding.ChannelType)` ever runs. The verifier resolution is already per-channel-type; only the header *extraction* is Shopee-bound. WebhooksController.cs must be in the Lazada unit's scope.

**Multi-channel engine reality (ADV-003):**
- The StockSync engine fans out to the global `ActiveChannels` config (via `ChannelLookupPort`); there is **no per-SKU channel mapping**. So the original U5 edge case "a SKU mapped on only one channel pushes to only that channel" tests a feature that does not exist. Dropped. The honest multi-channel proof: with Shopee + Lazada both active, one stock change produces a push intent for **both** active channels through the same pipeline.

---

## Scope Boundaries

- **Writing the three stub harnesses — IN** (the user's decision; reverses the original "reuse only"). Each is built on existing fixtures/drivers, not from scratch.
- **Lazada — IN** (the 2nd channel; makes multi-channel honest + demonstrates extensibility) + the channel-agnostic webhook-signature fix it requires.
- **TikTok adapter — out** (diminishing returns after the 2nd adapter proves the architecture).
- **Full cross-channel allocation engine (rebalance-on-quota-exhaustion across channels) — out.** R10 proves a 2nd channel flows through the engine; rule-weighted cross-channel allocation stays deferred.
- **Per-SKU channel mapping — out.** The engine fans out to global active channels; per-SKU routing is a feature the multi-channel proof does NOT assert (ADV-003).
- **Oversell-compensation flow (seller-notified cancellation) — out.** Oversell *detection* correctness is the reservation-ledger proof; the compensation flow defers.
- **Analytics module — out.**
- **Demo video, Swagger-per-service, all-six-services-perfect boot, production deployment — out** (per origin).
- **Dev-stack re-architecture beyond "boots enough to explore" — out.**
- **New net-new proof coverage beyond the three named stubs — out.** The three harnesses encode the invariants the stubs already named; no additional hard-problems are invented.

### Deferred to Follow-Up Work

- TikTok adapter + full cross-channel allocation/rebalance + oversell-compensation flow + per-SKU channel mapping → a "multi-channel completion" workstream after this finish line.
- Analytics module (CQRS read model + KPIs + CSV streaming) → separate roadmap-completion workstream.
- `PickerFixture` ↔ `HandoffFixture` consolidation (KTD4 carry from Sprint-12) → after the harnesses land.

---

## Context & Research

### Relevant Code and Patterns

**Proof estate — two tiers (see Verification Findings for evidence):**

*Tier 1 — real, make-runnable (U1 opt-in + U2 discovery):*
- `tests/ShopFlow.Inventory.IntegrationTests/MultiTenantScaleGateTests.cs` (oversell, AE1) — real, NOT Skip-marked.
- `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` (oversell invariants, AE1) — real FsCheck, `Category=Integration`.
- `tests/ShopFlow.SharedKernel.IntegrationTests/CrossTenantRoutingTests.cs` (tenant routing, part of AE3) — real.
- `tests/ShopFlow.Auth.IntegrationTests/Authorization/CrossTenant403Test.cs` (cross-tenant 403, part of AE3) — real body, Skip-marked, fixture-seeding to verify.

*Tier 2 — empty stubs, write the harness (U3/U4/U5):*
- `tests/ShopFlow.StockSync.IntegrationTests/MultiTenantStockSyncScaleGateTests.cs` (noisy-neighbor, AE2) → U3 (LIGHT).
- `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` (cross-role denial, AE4) → U4 (MEDIUM).
- `tests/ShopFlow.Auth.IntegrationTests/AuthCrossTenantTests.cs` (tenant isolation, AE3) → U5 (HEAVY).

**Fixtures + drivers that the harnesses build on (already real):**
- `tests/ShopFlow.StockSync.IntegrationTests/StockSyncTenantFixture.cs`, `Drivers/TenantBurstDriver.cs`, `Drivers/FakeChannelAdapterFactory.cs`, `FairnessCalculator.cs`, and the working `StockSyncHappyPathTests.cs` (the U3 template).
- `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs` (real WAF; all 4 role JWT builders wired; migrate+seed commented out at lines 164-186 — U4 implements them), `Authorization/NarrowedJwtBuilder.cs`, `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` (the seed `SeedAsync` U4's fixture calls), `OwnerSeed`.
- `tests/ShopFlow.Auth.IntegrationTests/AuthTenantFixture.cs` (repo-level provisioning U5 reuses for DB creation), `Authorization/AuthAdminAuthorizationFixture.cs` + `CrossTenant403Test.cs` (the WAF pattern U5's multi-tenant fixture models).

**Channel adapter pattern (mirror for Lazada):**
- `src/Services/Channel/ShopFlow.Channel.Application/Adapters/IChannelAdapter.cs` — the contract (`ChannelType`, `ParseWebhook`, `ParseOrderCreated`, stock-push).
- `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeAdapter.cs` — stateless reference adapter (parser + Polly pipeline + typed HttpClient).
- `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ChannelAdapterFactory.cs` — **DI-enumeration indexed by `ChannelType`**; a new adapter registered in DI resolves with ZERO factory change. This is the extensibility claim, demonstrable by construction.
- `src/Services/Channel/ShopFlow.Channel.Infrastructure/Signature/ShopeeSignatureVerifier.cs` — HMAC verifier (independent from the mock's signer, by design, to surface drift). `ISignatureVerifierFactory.Resolve(channelType)` already resolves per channel-type.
- `src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs` — the receiver; **line 72 hardcodes the Shopee signature header** — U7 makes extraction channel-agnostic (K8).
- `tools/mocks/shopee/` — separate Kestrel mock process (HMAC-signed webhooks, chaos endpoints) wired into AppHost via `AddProject<>` — mirror for `tools/mocks/lazada/`.
- `AddChannelModule` registration (Polly retry pipeline + typed HttpClient per adapter) — the "one line + wiring" site for Lazada.

**Dev-stack repair starting point (committed `acc2c91`):** launchSettings.json, pgbouncer image→bitnamilegacy, pgbouncer `POSTGRESQL_*` env, migrate `workingDirectory`, AddPackerRole migration-ordering fix. Remaining issues + the full first-boot findings: [docs/solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md](../solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md).

### Institutional Learnings

- [docs/solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md](../solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md) — the dev-stack first-boot chain + remaining items (pgbouncer config clobber, app-role/catalog ordering, service startup). Directly drives U6.
- [docs/solutions/2026-05-10-ef-migration-needs-attributes.md](../solutions/2026-05-10-ef-migration-needs-attributes.md) — any Lazada migration (if a channel row/seed is needed) carries `[Migration]`+`[DbContext]`, and must sort AFTER the latest existing migration on that DbContext (the Sprint-13 ordering lesson).
- Sprint-4 / Sprint-4.5 sign-offs — the Shopee adapter + webhook receiver + product-mapping + mock-server pattern that U7 mirrors.
- Sprint-1-redux / Sprint-3-redux / Sprint-5 integration-test patterns — the real fixtures U2/U3 lean on.

### External References

None — the channel-adapter, Aspire, xUnit/Testcontainers, and PgBouncer layers are all well-established locally and were debugged hands-on this session. Lazada's webhook/signature shape is mirrored from the existing Shopee mock pattern (mock fidelity, not real Lazada API integration — consistent with the project's "mock the hard parts" stance).

---

## Key Technical Decisions

- **K1. PgBouncer → `edoburu/pgbouncer` for both dev AppHost and prod compose.** It honors a mounted `pgbouncer.ini` directly (no entrypoint auto-config), sidestepping the bitnami clobber that made the bind-mounted `[databases]` config get ignored. Resolves the brainstorm's pgbouncer fork. Removes the bitnami-specific `POSTGRESQL_*` env added in `acc2c91`.
- **K2 (REVISED). The proof estate is two-tier; the three empty-stub harnesses are written, not reused.** The original plan assumed all proofs were complete-but-Skip'd; source verification proved three are `Task.CompletedTask` stubs. So: (a) Tier-1 real proofs get the env-gated opt-in and a local-green discovery pass (U1/U2); (b) Tier-2 stubs get real bodies written on top of the fixtures/drivers that already exist (U3/U4/U5). This reverses the original K2's "do NOT author new proof coverage" — but the harnesses encode invariants the stubs already named (no new hard-problems invented), so the scope is "fill the named gaps," not "expand the proof surface."
- **K3. Lazada demonstrates extensibility by construction.** The DI-enumeration `ChannelAdapterFactory` needs ZERO change; Lazada = new `LazadaAdapter` + parser + signature verifier + one DI registration + a mock server. The README routes to "Shopee + Lazada adapters, factory unchanged" as the proof of the plugin claim.
- **K4. Multi-channel proof scope = "2nd channel flows through the engine," not full cross-channel allocation, and not per-SKU routing.** The engine fans out to the global `ActiveChannels` set (no per-SKU mapping exists). R10's test proves one stock change pushes to BOTH active channels (Shopee + Lazada) through the same coalescing/rate-limit/priority/breaker pipeline. Rule-weighted allocation/rebalance and per-SKU channel mapping stay deferred (the original U5 "single-channel SKU" edge case is dropped — ADV-003).
- **K5. README changelog → history doc; top reframed to engineer-routing.** The 22-sprint changelog moves to a `docs/` history file (or a collapsed section); the README top becomes hard-problems routing + architecture + how-to-run + how-to-run-the-proofs + forward-looking.
- **K6 (HARDENED). `task up` = "boots enough to explore" with a NAMED service floor, not "dashboard only."** Time-boxed dev-stack repair (pgbouncer + app-role/catalog ordering + service startup). The fallback floor is concrete: **the Gateway + at least one real ShopFlow API the README routes to (target: Inventory.Api) serve `/health` 200 AND answer one real authenticated GET** — because the Testcontainers proofs don't exercise the Aspire services, the boot must still leave a real HTTP surface an evaluator can poke (ADV-004). If a service is genuinely intractable in the time-box, document which and why; the floor is never "dashboard alone."
- **K7. Lazada signature verifier independent from the Lazada mock's signer** (mirrors the Shopee pattern) so config drift surfaces as a test failure rather than silent agreement.
- **K8 (NEW). Webhook signature extraction becomes channel-agnostic.** `WebhooksController` stops hardcoding `[FromHeader(Name = "X-Shopee-Signature")]`. The receiver reads the signature in a channel-neutral way (e.g., the resolved adapter/verifier declares its header name, or the controller passes the full header collection to the verifier) so Lazada's signature header reaches `LazadaSignatureVerifier` instead of 401-ing as a null Shopee header. Closes SEC-U4-001. Shopee's existing behavior must be preserved (regression-pinned).
- **K9 (NEW). The harnesses build on existing scaffolding; effort is sized, not uniform.** U3 (StockSync noisy-neighbor) is LIGHT — extend the proven `StockSyncHappyPathTests` WAF + `TenantBurstDriver` + `FairnessCalculator` to 5 tenants + sustained burst. U4 (cross-role denial) is MEDIUM — implement `HandoffFixture`'s commented-out migrate+seed (lines 164-186), JWT builders already exist. U5 (Auth cross-tenant) is HEAVY — `AuthTenantFixture` is repo-level only, so build a multi-tenant Auth WAF fixture (model on `HandoffFixture` + `CrossTenant403Test`'s WAF). Honest sizing prevents the "flip a Skip" illusion that this revision corrects.

---

## Open Questions

### Resolved During Planning

- Which tests are "the proofs," and which are real vs hollow — resolved (Verification Findings; two-tier estate).
- Whether to write new harnesses — resolved: yes, the three named stubs (K2 revised, user decision).
- The pgbouncer fork — resolved per K1 (edoburu).
- Whether the factory changes for Lazada — resolved: no (DI-enumeration; K3).
- Whether the webhook receiver needs a change for Lazada — resolved: yes, channel-agnostic signature extraction (K8).
- Multi-channel proof shape — resolved: both-active-channels fan-out, no per-SKU routing (K4; ADV-003 edge case dropped).

### Deferred to Implementation

- **[Affects U2][Needs research]** Do the Tier-1 scale gates / property suite pass locally as-is, or need fixture/timing/port adjustments? Never run locally — U2 discovery.
- **[Affects U2/U5][Technical]** Does `AuthAdminAuthorizationFixture` actually provision tenant-A + tenant-B (the `CrossTenant403Test` body assumes it)? Verify at run; implement the two-tenant seeding if missing.
- **[Affects U3][Technical]** Does `TenantBurstDriver` expose a sustained-burst method, or only `EmitOneAsync`? If only single-emit, add a bounded burst loop in the test (not the driver) to keep the driver's contract stable.
- **[Affects U4][Technical]** The exact seed sequence for `HandoffFixture` (Auth migrate → Outbound migrate → OwnerSeed → RolePermissionsSeed → 4 user INSERTs) — the comments name it; confirm the migration ordering (incl. the Sprint-13 `AddPackerRole`) applies cleanly to a fresh tenant DB.
- **[Affects U5][Technical]** Which of the 5 R32 scenarios genuinely need a WAF (cross-tenant 401, MFA-reset target isolation) vs are DB-layer-tautological (user-list, role-perm isolation)? Build the WAF only for the ones that need it; keep the rest repo-level via `AuthTenantFixture`.
- **[Affects U6][Technical]** How deep is the service-startup (#8) issue — does the K6 floor (Gateway + Inventory.Api) come up cleanly, or need per-service diagnosis? Resolve at U6 against the named floor.
- **[Affects U7][Technical]** Lazada mock webhook + signature header shape — mirror Shopee's HMAC pattern; exact signing-string fields + header name are a U7 detail (mock fidelity, not real Lazada API).
- **[Affects U7][Technical]** The cleanest channel-agnostic signature-extraction shape (verifier-declares-header vs pass-all-headers) — U7 picks the lowest-churn form that preserves Shopee behavior.
- **[Affects U1][Technical]** Exact conditional-skip mechanism (custom `FactAttribute` subclass vs `Skip` set from a static env check vs an xUnit `ITestCondition`) — U1 picks the lowest-friction form that keeps CI behavior identical.

---

## Implementation Units

Dependency shape: U1 → U2; U1 → U3, U4, U5 (each writes a stub body behind the U1 gate; mutually independent); U6 independent (dev-stack; proofs use Testcontainers, not Aspire); U7 → U8; U9 depends on U2 + U3 + U4 + U5 + U6 + U7 + U8 (routes only to verified-working things); U10 last.

### U1. Proof-run opt-in mechanism

**Goal:** Make the proof suites runnable locally on demand without disturbing the default `dotnet test` / CI posture. Establish the gate once; the harness-writing units (U3-U5) apply it to their files as they fill the bodies.

**Requirements:** R1, R3.

**Dependencies:** None.

**Files:**
- Create: a shared conditional-skip helper (e.g., `tests/_shared/ProofGate.cs` or a small attribute in an existing shared test utility project) — exact home chosen at execution.
- Modify (Tier-1, real bodies — gate now so U2 can run them): `MultiTenantScaleGateTests.cs`, `ReservationLedgerProperties.cs`, `CrossTenantRoutingTests.cs`, `Authorization/CrossTenant403Test.cs`.
- (Tier-2 files `MultiTenantStockSyncScaleGateTests.cs`, `AuthCrossTenantTests.cs`, `Handoff/CrossRoleDenialTests.cs` get the gate applied by U3/U4/U5 when their bodies are written — gating an empty stub now would only produce a misleading "opt-in green.")
- Modify: `Taskfile.yml` (add a `proofs` target that sets the opt-in env + runs the four categories).
- Test: the gate helper's own behavior.

**Approach:** A single env-checked skip condition (K2). Default (env unset) → skipped, identical to today's CI posture. Env set (via `task proofs`) → the tests run. Keep the existing `Category=*` traits so the filters still work. The `MultiTenantScaleGateTests` is currently NOT Skip-marked (it fails via fixture when Docker is absent) — bring it under the same gate so a no-Docker default run skips it cleanly rather than erroring.

**Patterns to follow:** existing `[Fact(Skip = "...")]` usages; `Taskfile.yml` `test:*` targets.

**Test scenarios:**
- Happy path: with the opt-in env unset, a gated proof reports Skipped; with it set, the test executes (verify via one representative Tier-1 proof).
- Edge case: `Category` filters still select the tests in both states (no trait regression).
- Edge case: `MultiTenantScaleGateTests` no longer hard-errors on a no-Docker default run (it skips).
- Verification: `task proofs` runs the categories; default `task test:unit` and a plain `dotnet test` are unchanged.

**Verification:** `task proofs` attempts to execute (not skip) the Tier-1 proofs; CI-equivalent default run still skips them; no Docker-absent hard error in the default run.

### U2. Get the Tier-1 (real) proofs green locally — discovery

**Goal:** Run the genuinely-real proof suites via the U1 opt-in against Docker/Testcontainers and fix whatever breaks until green — the first time they've ever run on a dev machine.

**Requirements:** R1, R2.

**Dependencies:** U1.

**Files:** the Tier-1 fixtures + any fixes — `tests/ShopFlow.Inventory.IntegrationTests/`, `tests/ShopFlow.PropertyTests/`, `tests/ShopFlow.SharedKernel.IntegrationTests/`, `tests/ShopFlow.Auth.IntegrationTests/Authorization/` (incl. `AuthAdminAuthorizationFixture` two-tenant seeding if `CrossTenant403Test` needs it).

**Approach:** Run-first/characterize. The integration proofs are Testcontainers-based (independent of `task up`). The `Category=Load` oversell scale gate has NEVER run locally and is the real risk (timing budgets, port allocation, container-pool limits, fixture warm-up). Fix to runnability, not re-architecture — if a budget is CI-tuned, adjust the local budget rather than rewriting the harness. For `CrossTenant403Test`, confirm `AuthAdminAuthorizationFixture` provisions both tenants; implement the seeding if the comment over-promises.

**Execution note:** Run each proof first to capture the actual failure, then fix — same discipline that surfaced the dev-stack chain and the migration-ordering bug. Expect discovery; do not assume green.

**Test scenarios:**
- `Covers AE1.` Oversell scale gate green locally: 5 tenants × 1,000 concurrent reservations, zero oversell, fairness ≥ 0.85.
- `Covers AE1.` Reservation-ledger FsCheck property suite green locally (all 5 properties).
- `Covers AE3 (part).` `CrossTenantRoutingTests` + `CrossTenant403Test` green (403/401 + zero cross-tenant access; `CrossTenant403Test` fixture provisions two real tenant DBs).

**Verification:** `task proofs` → the Tier-1 groups pass locally; each failure encountered is fixed (or, if genuinely environment-bound, documented in the run-locally doc with the constraint).

### U3. Write the StockSync noisy-neighbor harness (LIGHT)

**Goal:** Replace the two empty `MultiTenantStockSyncScaleGateTests` stubs with a real noisy-neighbor proof — one tenant bursts stock changes while the others hold their SLO — built on the proven happy-path scaffolding.

**Requirements:** R1, R2, R3.

**Dependencies:** U1 (gate).

**Files:**
- Modify: `tests/ShopFlow.StockSync.IntegrationTests/MultiTenantStockSyncScaleGateTests.cs` (fill the two bodies; apply the U1 gate).
- Reuse: `StockSyncHappyPathTests.cs` (WAF + multi-tenant catalog provisioning template), `Drivers/TenantBurstDriver.cs`, `Drivers/FakeChannelAdapterFactory.cs`, `FairnessCalculator.cs`, `StockSyncTenantFixture.cs`.
- Possibly modify: `TenantBurstDriver` only if a sustained-burst helper is cleaner there than in the test (keep the driver contract stable if possible).

**Approach:** Extend the happy-path harness to N tenants (5): provision each tenant DB + register in the control-plane catalog, boot one `WebApplicationFactory<Program>` with the `FakeChannelAdapterFactory` recorder, then drive tenant A with a sustained burst (e.g., 2k changes over a bounded window) while B-E emit a steady trickle. Assert B-E push latency p99 stays under the SLO and the per-tenant fairness floor (`FairnessCalculator`) ≥ 0.85, and A's bursts coalesce. Use a bounded, CI-friendly burst volume + wall-time budget (mirror the happy-path's polling-budget discipline) — this is a correctness/fairness proof, not a benchmark.

**Execution note:** Run-first — the happy-path proves the pipeline works for one tenant; the discovery is whether per-tenant isolation (queue + token bucket + breaker) actually holds the fairness floor under a real burst. If it doesn't, that's a genuine finding the test pins.

**Test scenarios:**
- `Covers AE2.` Noisy-neighbor: tenant A bursts; B-E hold push-latency p99 SLO; fairness ≥ 0.85.
- Edge case: A's rapid same-SKU changes coalesce per `(tenant, sku, channel)` (last-write-wins), so A's downstream push count is bounded, not 1:1 with emits.
- Edge case: breaker/token-bucket isolation — A tripping its own channel breaker does not stall B-E's dispatch.

**Verification:** `task proofs` → `MultiTenantStockSyncScaleGateTests` executes (not skipped) and passes locally; fairness + p99 asserted with real numbers logged.

### U4. Write the cross-role denial harness (MEDIUM)

**Goal:** Replace the empty `CrossRoleDenialTests` (and the `HandoffWorkflowTests` happy-path) stubs with real proofs that the 4-role RBAC stack rejects cross-role actions with 403 and leaves saga state unchanged — by implementing `HandoffFixture`'s commented-out seeding.

**Requirements:** R1, R3.

**Dependencies:** U1 (gate).

**Files:**
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs` (implement the 5 commented seed steps at lines 164-186: Auth schema migrate → Outbound schema migrate → `OwnerSeed.SeedAsync` → `RolePermissionsSeed.SeedAsync` → raw INSERT of Owner/Picker/Dispatcher/Packer users).
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` (fill the denial bodies; apply the U1 gate).
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` (fill the 4-role hand-off happy-path body — Picker→Packer→Dispatcher).
- Reuse: `NarrowedJwtBuilder`, `RolePermissionsSeed` (`PickerBaseline`/`DispatcherBaseline`/`PackerBaseline`), the already-wired `BuildOwnerJwt`/`BuildPickerJwt`/`BuildDispatcherJwt`/`BuildPackerJwt`.

**Approach:** The WAF + JWT minting already work; the gap is the fixture seeding (DB tables don't exist on a fresh tenant DB until the migrations run). Implement the commented steps, confirm the migration ordering applies cleanly (incl. Sprint-13 `AddPackerRole` widening `chk_role_permissions_role` before the Packer INSERT). Then each denial test is a JWT-authenticated POST to a transition endpoint asserting 403 + the saga/order state is unchanged afterward. Mirror the cross-role matrix the stubs already named (e.g., Picker→pack/ship, Dispatcher→pick/pack, Packer→pick/ship each 403; plus the adversarial-F3 ordering pin: wrong-role + wrong-state asserts `auth.forbidden` not `order.invalid_state`).

**Execution note:** Run-first against the newly-seeded fixture — the discovery is whether the per-action `[Authorize(Policy=...)]` gates + tenant binding actually reject as designed once a real multi-role tenant is seeded.

**Test scenarios:**
- `Covers AE4.` Cross-role denial matrix: each non-owning role attempting another role's transition → 403, saga state unchanged.
- Happy path: 4-role hand-off (Picker confirms pick → Packer confirms pack → Dispatcher confirms ship) drives one order through the saga (`HandoffWorkflowTests`).
- Edge case (adversarial-F3): wrong role + wrong pre-state returns `auth.forbidden` (auth filter fires before the controller state check), not `order.invalid_state`.
- Edge case (adversarial-F8): a Picker with an operator-granted extra key CAN perform that action (the additive-only contract has no defense-in-depth surprise rescue) — pins the KTD1 consequence.

**Verification:** `task proofs` → `CrossRoleDenialTests` + `HandoffWorkflowTests` execute (not skipped) and pass locally; the fixture seeds a real 4-role tenant.

### U5. Write the Auth cross-tenant isolation harness (HEAVY)

**Goal:** Replace the 5 empty `AuthCrossTenantTests` stubs with real tenant-isolation proofs, building the multi-tenant Auth WAF fixture that was never written.

**Requirements:** R1, R3.

**Dependencies:** U1 (gate).

**Files:**
- Create: a multi-tenant Auth WAF fixture (e.g., `tests/ShopFlow.Auth.IntegrationTests/MultiTenantAuthFixture.cs`) — `WebApplicationFactory<Program>` over `Auth.Api` + control-plane catalog + two provisioned tenant DBs, modeled on `HandoffFixture` (WAF shape) + `AuthTenantFixture` (DB provisioning) + `Authorization/AuthAdminAuthorizationFixture` + `CrossTenant403Test` (the existing WAF cross-tenant pattern).
- Modify: `tests/ShopFlow.Auth.IntegrationTests/AuthCrossTenantTests.cs` (fill the 5 bodies; apply the U1 gate).
- Reuse: `NarrowedJwtBuilder`, `RolePermissionsSeed`, `OwnerSeed`.

**Approach:** Build the WAF fixture once (the heavy part). Then map the 5 R32 scenarios to the cheapest correct level (deferred Open Question): cross-tenant 401 + MFA-reset target isolation need the WAF + tenant routing; user-list isolation + role-perm isolation are near-tautological at the DB layer (each tenant's rows live in its own DB) and can stay repo-level via `AuthTenantFixture` if that's lighter. Reuse `CrossTenant403Test`'s proven "X-Tenant header forces tenant-B, JWT claims tenant-A → never 200" technique for the routing scenarios.

**Execution note:** Run-first — once the WAF fixture provisions two tenants, the discovery is whether the tenant routing middleware + per-tenant DbContext binding actually isolate as designed (the PDPA hard-isolation claim). Capture real responses.

**Test scenarios:**
- `Covers AE3 (part).` Same-tenant request with a valid aligned JWT succeeds; cross-tenant request (JWT tenant-A, routed tenant-B) never returns 200 (401/403).
- Isolation: an admin user-list query in tenant-A never returns tenant-B users.
- Isolation: a role-permissions read in tenant-A reflects only tenant-A's `role_permissions`.
- Isolation: an admin MFA-reset targeting a user id resolves only within the caller's tenant (cannot reset a tenant-B user).

**Verification:** `task proofs` → `AuthCrossTenantTests` execute (not skipped) and pass locally; the multi-tenant Auth WAF fixture provisions ≥ 2 real tenant DBs.

### U6. `task up` minimal repair — boots enough to explore

**Goal:** Close the remaining dev-stack issues to the K6 named-floor bar so an engineer's clone-and-run reaches the dashboard + a real, pokeable HTTP service.

**Requirements:** R6.

**Dependencies:** None (independent of the proof units — proofs use Testcontainers, not the Aspire stack).

**Files:**
- Modify: `src/AppHost/ShopFlow.AppHost/Program.cs` (pgbouncer → edoburu per K1; remove bitnami-specific `POSTGRESQL_*` env), `src/AppHost/ShopFlow.AppHost/PgBouncerConfig.cs` (if the rendered config path/shape changes for edoburu).
- Modify: `infrastructure/docker-compose.yml` + `infrastructure/docker-compose.prod.yml` (pgbouncer image consistency with K1).
- Modify: provisioning ordering for the `shopflow_app` role vs catalog (`tools/shopflow-migrate/Provisioning/` — ensure the app-role exists before any connection that authenticates as it; the catalog-vs-app-role chicken-and-egg from the solutions note).
- Investigate/modify: Gateway + Inventory.Api (the K6 floor) + others' API startup (#8) to the named-floor bar.

**Approach:** Apply K1 (edoburu respects the bind-mounted `pgbouncer.ini`, so the `[databases]` routing for `shopflow_control` + tenants actually takes effect). Fix the app-role ordering so catalog provisioning can authenticate. Diagnose service startup via the Aspire dashboard resource view / per-service logs. K6 named floor: land "Gateway + Inventory.Api serve `/health` 200 + one real authenticated GET works"; document any service left not-booting and why.

**Execution note:** Run-first/characterize — boot the stack, read the dashboard resource states + crash logs, fix iteratively. This is live-infra discovery, not paper design.

**Test scenarios:**
- `Covers AE6.` Integration/smoke: with Docker running, the AppHost boots; the Aspire dashboard is reachable; Gateway + Inventory.Api serve `/health` 200 and one real GET (e.g., an inventory read) returns through the gateway. (Verified by booting, not a unit test — record the outcome.)
- Edge case: the existing `MigrationSmokeTests` / provisioning still pass with the edoburu + role-ordering changes.
- Integration: a tenant provisions end-to-end through the AppHost migrate chain (catalog + dev tenants `Ready`) — the flow that failed pre-repair.

**Verification:** `task up` reaches the dashboard + the K6 named service floor (real GET works); tenant provisioning completes through the orchestrator; prod compose references the same pgbouncer image. Any service left not-booting is documented per K6.

### U7. Lazada channel adapter end-to-end + channel-agnostic webhook signature

**Goal:** Implement Lazada as the second channel — adapter + parser + signature verifier + DI registration + mock server + AppHost wiring — and make the webhook receiver's signature extraction channel-agnostic so Lazada webhooks actually verify. No change to the `IChannelAdapter` framework/factory contract.

**Requirements:** R9, R11.

**Dependencies:** None for the adapter+tests; AppHost wiring benefits from U6 but doesn't block on it.

**Files:**
- Create: `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/LazadaAdapter.cs`, a `LazadaWebhookParser`, `src/Services/Channel/ShopFlow.Channel.Infrastructure/Signature/LazadaSignatureVerifier.cs` (mirror the Shopee trio).
- Modify: `src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs` — **channel-agnostic signature extraction (K8)**: stop hardcoding `[FromHeader(Name="X-Shopee-Signature")]`; read the signature per resolved channel-type (verifier-declares-header or pass-all-headers). Preserve Shopee behavior.
- Modify: `AddChannelModule` registration (register `LazadaAdapter` + its typed HttpClient + Polly pipeline + `LazadaSignatureVerifier` in the verifier factory — the "one line + wiring").
- Create: `tools/mocks/lazada/` (mirror `tools/mocks/shopee/` — Kestrel mock with signed webhooks + chaos endpoints).
- Modify: `src/AppHost/ShopFlow.AppHost/Program.cs` (`AddProject<>` for the lazada mock, mirroring shopee-mock).
- Test: `tests/ShopFlow.Channel.UnitTests/` (Lazada parser + signature verify + ParseOrderCreated + a Shopee-signature-still-works regression pin for the controller change) + a mock round-trip integration test mirroring the Shopee one.

**Approach:** Stateless adapter mirroring `ShopeeAdapter` (K3). `ChannelType => "lazada"`. The factory is untouched — registering the adapter in DI is sufficient for `ResolveFor("lazada")`. Make the controller's signature extraction channel-neutral (K8) — the existing `ISignatureVerifierFactory.Resolve(channelType)` is already per-channel; only the header read is Shopee-bound. Lazada's webhook/signature shape mirrors the Shopee mock's HMAC pattern (K7 — verifier independent from the mock's signer). Reuse the channel-agnostic `HybridProductMappingService` for Lazada SKU mapping.

**Patterns to follow:** `ShopeeAdapter.cs`, `ShopeeWebhookParser`, `ShopeeSignatureVerifier.cs`, `WebhooksController.cs`, `tools/mocks/shopee/`, the `AddChannelModule` Shopee registration, the Sprint-4 Shopee mock round-trip integration test.

**Test scenarios:**
- Happy path: `LazadaAdapter.ChannelType == "lazada"` and `ChannelAdapterFactory.ResolveFor("lazada")` returns it with no factory edit.
- Happy path: a valid signed Lazada webhook parses to a `WebhookEnvelope` / `ExternalOrderDraft` (order-created gating + field extraction), and its signature header reaches `LazadaSignatureVerifier` (K8 — not a null Shopee header).
- Regression: a valid signed Shopee webhook still verifies after the K8 controller change (no Shopee behavior change).
- Error path: a Lazada webhook with a bad signature is rejected by `LazadaSignatureVerifier` (constant-time compare; mirrors Shopee).
- Error path: a non-order-created Lazada event is gated out (no downstream emit).
- Integration: `Covers AE7.` mock round-trip — the lazada mock signs a webhook, the receiver verifies + parses + persists with `(channel_id, provider_event_id)` idempotency in the tenant DB; a replay of the same event yields exactly one order.

**Verification:** Lazada adapter unit tests green; the Shopee regression pin green; the Channel mock round-trip integration test green (under the U1 opt-in if Docker-backed); `ChannelAdapterFactory` unchanged; `dotnet build` clean.

### U8. Multi-channel sync proof

**Goal:** Prove the product story — a stock change for a SKU pushes to BOTH active channels (Shopee + Lazada) through the existing sync engine.

**Requirements:** R10.

**Dependencies:** U7.

**Files:** a new test in `tests/ShopFlow.StockSync.IntegrationTests/` (or `tests/ShopFlow.Channel.IntegrationTests/`) exercising a two-channel push; reuse the existing StockSync engine + the U7 Lazada adapter + the Shopee adapter + the proven `StockSyncHappyPathTests` harness shape (`FakeChannelAdapterFactory` recorder across two channels).

**Approach:** Configure `ActiveChannels = [shopee, lazada]`; emit one stock change for a SKU; assert the sync engine produces a push intent for **each** active channel through the same coalescing/rate-limit/priority/breaker pipeline. Per K4, this proves multi-channel fan-out, NOT cross-channel allocation/rebalance and NOT per-SKU routing (the engine has no per-SKU channel mapping — the original "single-channel SKU" edge case is dropped, ADV-003).

**Execution note:** Write the failing two-channel assertion first against the existing engine; it should pass once Lazada is a registered active channel — if it doesn't, the gap is the engine's per-channel fan-out, which the test then pins.

**Test scenarios:**
- Integration: `Covers AE8.` one stock change with both channels active → a push intent recorded for Shopee AND for Lazada, each through the rate-limited/coalesced pipeline.
- Edge case: coalescing still holds per `(tenant, sku, channel)` — rapid changes collapse to the latest per channel, independently for the two channels.
- Edge case: one channel's breaker tripping does not suppress the other channel's push (per-channel isolation).

**Verification:** the multi-channel push test is green (under the U1 opt-in); it is added to the proof set the README routes to.

### U9. README reframe + run-locally doc

**Goal:** Turn the first artifact an engineer meets into a router to the impressive engineering, and document an accurate clone-and-run path.

**Requirements:** R4, R5, R7, R11.

**Dependencies:** U2, U3, U4, U5, U6, U7, U8 (route only to verified-working proofs/flows + the real run story).

**Files:**
- Modify: `README.md` (top reframed: hard-problems table with code path + proving-test path + run command — oversell scale gate, noisy-neighbor, tenant isolation, cross-role RBAC, plus the Shopee+Lazada pluggability + multi-channel push rows; architecture diagram; how-to-run; how-to-run-the-proofs via `task proofs`; forward-looking "100+ tenants / SOC2" section).
- Create: a history doc (e.g., `docs/sprint-history.md`) — move the sprint-by-sprint changelog out of the README.
- Create/modify: a run-locally doc (Docker prerequisite + native-Postgres-port coexistence note + `task up` K6 named-floor expectation + `task proofs` opt-in) — standalone or a README section.

**Approach:** Lead with the four hard-problems (now all really runnable) + the pluggable-channel story, each linking code ↔ proving test ↔ run command (K5). Only route to proofs that U2-U8 made actually green. Keep the changelog accessible but out of the lead. State prerequisites honestly so a stranger's clone-and-run matches reality (R7).

**Test scenarios:** `Test expectation: none — documentation.` Verified by the AE6 clone-and-run check (U6) + a read-through that each routed link resolves to a real, green proof.

**Verification:** `Covers AE5.` README top names the four hard-problems + the Shopee/Lazada pluggability + multi-channel push, each with code + proving-test + run-command links; the changelog is no longer the lead; the run-locally prerequisites are accurate.

### U10. Clean-repo + final verification

**Goal:** Confirm the finish line holds end to end.

**Requirements:** R8.

**Dependencies:** U2, U3, U4, U5, U6, U7, U8, U9.

**Files:** repo-wide verification; no new behavior. A sign-off doc under `docs/phase-gates/`.

**Approach:** Confirm no local-run hacks are committed (the 5432/bypass hacks were reverted; the genuine fixes are in `acc2c91` + this plan's commits), git history is coherent, `dotnet build` is clean, the full unit suite passes, all proofs (Tier-1 + the three newly-written harnesses + the multi-channel proof) run green via `task proofs`, and `task up` boots to the K6 named floor.

**Test scenarios:** `Test expectation: none — verification gate.`

**Verification:** `dotnet build ShopFlow.sln` → 0/0; unit suite green; `task proofs` → all hard-problem groups (incl. the three written harnesses + multi-channel) green and **executing, not skipped**; `task up` → dashboard + Gateway + Inventory.Api real GET; README routes resolve. Success criteria from origin met (an engineer can run the proofs + read the routing in ~15 min).

---

## System-Wide Impact

- **Interaction graph:** U1's conditional-skip helper touches the proof groups but changes only the skip trigger, not test bodies. U3/U4/U5 fill empty stub bodies using existing fixtures (StockSync WAF + burst driver; HandoffFixture seeding; a new Auth WAF fixture) — no production code changes. U7's `LazadaAdapter` enters via DI enumeration → `ChannelAdapterFactory` resolves it with no edit; the `WebhooksController` signature-extraction change is the one production touch on the receive path (channel-agnostic, Shopee-regression-pinned); `HybridProductMappingService` + the StockSync push pipeline are channel-agnostic and gain a second channel without structural change.
- **Error propagation:** U6's pgbouncer/role-ordering changes affect the dev boot path only; the proofs (Testcontainers) are unaffected. Lazada signature failures propagate as receiver rejections (mirrors Shopee), never reaching a DB; the K8 change must not weaken Shopee's rejection path.
- **State lifecycle risks:** U4 implements `HandoffFixture` seeding — confirm the Sprint-13 `AddPackerRole` migration ordering applies cleanly to a fresh tenant DB (the bug class this session already hit). U6 touches provisioning ordering (app-role vs catalog) — verify `MigrationSmokeTests` + a clean tenant provision still pass. U7/U8 add channel rows / product mappings — confirm idempotency keys hold per channel.
- **API surface parity:** Lazada mirrors the Shopee adapter surface exactly; no new `IChannelAdapter` contract members. The `WebhooksController` route shape is unchanged; only the signature-header read becomes channel-neutral.
- **Unchanged invariants:** `IChannelAdapter` framework + `ChannelAdapterFactory` contract (K3); default `dotnet test` / CI skip-posture (K2); the Tier-1 proof bodies (only their skip trigger changes); the committed Sprint-13 feature work; Shopee webhook verification (regression-pinned through K8).

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Tier-1 scale gate (`Category=Load`) never ran locally — may need real fixes | U2 is explicit run-first discovery; fix to runnability, adjust CI-tuned budgets for local, document genuinely environment-bound constraints rather than forcing green |
| **U5 (Auth cross-tenant) is the heaviest — a multi-tenant Auth WAF fixture doesn't exist** | Model on `HandoffFixture` (WAF) + `AuthTenantFixture` (provisioning) + `CrossTenant403Test` (cross-tenant technique); push DB-tautological scenarios to the lighter repo-level fixture; time-box and, if the WAF proves deep, land the routing scenarios that need it + keep the isolation scenarios repo-level |
| **U4 fixture seeding (HandoffFixture lines 164-186) may surface a migration-ordering or seed-ordering bug** | Run-first; the Sprint-13 `AddPackerRole` ordering already bit once — confirm the chain applies clean to a fresh DB before asserting the denial matrix |
| `CrossTenant403Test` fixture may not actually provision two tenants (comment over-promises) | U2 verifies at run; implement the two-tenant seeding in `AuthAdminAuthorizationFixture` if missing |
| Service startup (#8) turns out deep, balloons `task up` | K6 named floor: land Gateway + Inventory.Api (real GET) rather than all six; document the rest; never degrade to "dashboard only" |
| K8 channel-agnostic signature change regresses Shopee verification | Shopee-signature regression pin in U7 test scenarios; the verifier factory is already per-channel — only the header read changes |
| edoburu/pgbouncer config shape differs from bitnami's expectations | edoburu reads `/etc/pgbouncer/pgbouncer.ini` directly (the bind-mount target already used); the rendered `[databases]` + userlist already exist — verify auth_type/userlist format on first boot |
| Lazada mock fidelity drifts from a "realistic" marketplace | Mirror the Shopee mock's proven shape (HMAC + chaos endpoints); verifier independent from signer (K7) surfaces drift as a test failure |
| Multi-channel proof reveals the engine fan-out is single-channel-assuming | U8 writes the failing assertion first; if the engine needs a fan-out fix, the test pins it — scoped to flow, not allocation/per-SKU (K4) |
| Scope grew from 7 → 10 units (3 harnesses added) | Honest sizing per K9 (LIGHT/MEDIUM/HEAVY); U3 reuses proven scaffolding, U4 implements named-but-commented seeding, only U5 is net-new fixture work; the harnesses can land incrementally |

---

## Alternative Approaches Considered

- **Lean only on the 2 real proofs; leave the 3 stubs as labeled scaffolding (the lighter finish line).** Rejected by the user in favor of writing all three — maximal, honest proof coverage of the headline hard-problems (noisy-neighbor, tenant isolation, RBAC denial), not just the oversell/ledger pair. Accepts the larger scope.
- **Naively un-skip the stubs and call them green.** Rejected — they're `Task.CompletedTask` bodies; un-skipping proves nothing. This is the exact illusion this revision corrects.
- **Keep multi-channel deferred + make README honest about one channel (brainstorm option C).** Rejected earlier: a "multi-channel WMS" with one channel is a hollow headline an engineer spots immediately; the 2nd adapter is the cheapest honest fix and doubles as the extensibility proof.
- **Add Lazada + TikTok (3 channels).** Rejected: diminishing returns once the 2nd adapter proves the plugin architecture; TikTok was never mocked. Deferred.
- **Fight bitnami pgbouncer auto-config instead of switching to edoburu.** Rejected (K1): bitnami's entrypoint requires `POSTGRESQL_*` env that then triggers the auto-config clobber — a catch-22; edoburu honors the mounted config directly with less fragility.
- **Live-demo as the proof vehicle instead of tests.** Rejected at brainstorm time: an engineer trusts a green zero-oversell-under-load test over a clicked UI, and a noisy-neighbor burst can't be shown on a dashboard.

---

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md](../brainstorms/2026-05-27-portfolio-finish-line-requirements.md)
- Dev-stack findings: [docs/solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md](../solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md)
- Roadmap (phase structure): [docs/redesign/01-product-development-plan.md](../redesign/01-product-development-plan.md) §9
- Channel adapter pattern: `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeAdapter.cs`, `.../ChannelAdapterFactory.cs`, `src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs`, `tools/mocks/shopee/`
- Proof tests + fixtures: the two-tier estate listed under Context & Research + Verification Findings
- Committed dev-stack fixes: commit `acc2c91`
