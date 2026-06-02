---
title: Portfolio finish-line — prove the hard problems for an engineer evaluator
created: 2026-05-27
status: in-progress
origin: solo-brainstorm
actors: [Engineer-evaluator, Maintainer]
flows: [F1-engineer-evaluation, F2-clone-and-run-enough, F3-run-the-proofs]
---

# Portfolio finish-line — prove the hard problems for an engineer evaluator

## Summary

Define and reach the "finish line" for the ShopFlow WMS portfolio piece, scoped for an engineer/tech-interviewer who clones, runs, and reads code. "Done" means the signature hard-problems are *demonstrably true* via tests that run green on the evaluator's own machine, routed by an engineer-oriented README — AND the product's headline identity, **multi-channel sync**, is made real by a second channel adapter (Lazada) so "multi-channel" isn't a one-channel claim. Not feature-completeness, not a live demo, not all six services booting perfectly.

**Scope amendment (2026-05-27):** the initial finish-line draft deferred Lazada; on review, shipping a "multi-channel marketplace WMS" with only Shopee wired leaves the core product claim hollow and an engineer evaluator would spot it immediately. Lazada (the 2nd channel) is now **in** the finish line — it also *demonstrates* the plugin architecture (the "a new adapter is ~one line of DI registration" claim) rather than merely asserting it. TikTok remains deferred (diminishing returns after the 2nd adapter proves extensibility).

---

## Status update (2026-06-02) — verified progress + remaining path

This finish line is **~60% executed** on branch `feat/portfolio-finish-line` (HEAD `66b306a`). Status below was verified against source + git on 2026-06-02, not taken from the sprint-changelog narrative. Target bar **reconfirmed: portfolio-demo-done** — all four hard-problems green locally + an honest 2nd channel (Lazada) + an engineer-routing README. Commercial-grade build-out (Analytics, Inbound handlers, the ComingSoon pages, TikTok/Shopify) and a full live clickable demo stay **out** by design.

**Premise change since the original brainstorm.** The plan assumed "no local Docker; CI-only." **Docker now runs locally (v29.4.2)** and the U1–U4 proofs were confirmed green on this machine. The Dependencies/Assumptions line "the proving tests actually pass when run locally" is now *partly discharged* — and U5/U6/U8 are locally iterable rather than CI-roundtrip-bound (the single biggest tractability change).

**Done (verified on branch):**
- **U0** — brainstorm + plan (post-doc-review revision).
- **U1** — proof-run opt-in (`Category=Proof` + `SHOPFLOW_RUN_PROOFS` env gate).
- **U2** — Tier-1 proofs green locally (oversell scale gate, reservation-ledger FsCheck, cross-tenant routing 5/5); also fixed a production `JwtTokenIssuer` singleton-consumes-scoped DI bug.
- **U3** — StockSync noisy-neighbor harness (real `FairnessCalculator`; surfaced + fixed 8 never-ran composition bugs).
- **U4** — cross-role denial harness 14/14 green; made `Outbound.Api` boot (double-`AddMassTransit` + missing `ITenantCatalog`).
- **U6 (partial)** — `Auth.Api` + `StockSync.Api` + `Outbound.Api` boot fixes; service ControlPlane configs consolidated to `shopflow_app` via PgBouncer; app-role/catalog ordering.

**Remaining — the completion path, in dependency order:**
1. **U5 — Auth cross-tenant isolation harness (HEAVY; completes hard-problem #3).** Build the multi-tenant Auth WAF fixture; un-Skip `CrossTenant403Test` + `AuthCrossTenantTests` (today returns 500). This is the only one of the four headline proofs not yet genuinely green.
2. **U4 finish — 4-role hand-off happy-path (MEDIUM).** Un-Skip `HandoffWorkflowTests`; wire the saga drive-through (Picker→Packer→Dispatcher). Today only the *denial* half of AE4 is green; the *hand-off* half is still Skip-marked. **Confirmed IN scope (2026-06-02)** so AE4 is honestly "proven", not half-proven. Likely needs the deferred `POST /orders` + `/seed` 500 (`CreatedAtAction` async-suffix) fixed for the seed path.
3. **U7 — Lazada adapter + channel-agnostic webhook signature (L; makes multi-channel honest).** Adapter + parser + verifier + DI registration + mock server + the `WebhooksController` header-extraction fix (K8). Verified blocker: `WebhooksController` hardcodes the Shopee signature header → Lazada webhooks 401 before the per-channel verifier runs.
4. **U8 — multi-channel sync proof (MEDIUM; depends on U7).** One stock change for a dual-listed SKU → push to BOTH Shopee + Lazada through the existing coalescing/rate-limit/priority/breaker engine.
5. **U6 finish — `task up` to the K6 floor (MEDIUM).** edoburu PgBouncer swap (K1) + native-Postgres-5432 coexistence + verify `Notification.Api` startup; or document the residue as a named README prerequisite (K6 allows this for the not-live-demo bar).
6. **U9 — README reframe + run-locally doc (SMALL; lands late).** Lead with the four hard-problems (code ↔ proving-test ↔ run-command) + the Shopee/Lazada pluggability; demote the sprint changelog to a history doc.
7. **U10 — clean-repo + final verification + sign-off + tag (SMALL; last).**

**Sequencing rule:** proof + adapter work (U5, U4-finish, U7, U8, U6-finish) first; framing work (U9, U10) last — so the README never advertises a proof that isn't green yet, and isn't rewritten twice.

---

## Problem Frame

ShopFlow WMS has 22 green sprint tags, ~832 passing unit tests, and an exhaustive sign-off trail — but the first live boot (2026-05-27) proved the Aspire dev stack had **never actually run** on a developer machine. Five first-boot breakages were found; three remain. Meanwhile the artifact that an engineer evaluator first meets — the README — is a sprint-by-sprint changelog wall that buries the genuinely impressive engineering under release notes.

The cost of leaving it here is specific to the chosen audience: an engineer who clones the repo, runs `task up`, and hits a wall (or skims a changelog and never finds the hard parts) walks away *less* impressed than if there were no repo — a polished-looking project that doesn't deliver is worse than a modest one that does. The four hard-problems that actually differentiate this build (oversell-safe reservation ledger, noisy-neighbor multi-tenant sync, database-per-tenant isolation, defense-in-depth RBAC) are currently provable only by Skip-marked, CI-only tests an evaluator can't see or run, and are not surfaced anywhere an evaluator would look.

Separately, the product's **headline identity is hollow**: ShopFlow brands itself a "multi-channel WMS for SEA marketplaces (Shopee, Lazada, TikTok Shop)," but only the **ShopeeAdapter** exists — the `IChannelAdapter` framework + factory are pluggable yet carry exactly one plugin, and there's one mock server (Shopee). The noisy-neighbor sync engine (the genuinely hard part) is channel-agnostic and impressive, but "sync across multiple channels" is one channel real. An engineer evaluator reading "multi-channel sync engine" and finding a single adapter spots the gap immediately. A second adapter (Lazada) both makes the claim honest and *demonstrates* the plugin architecture rather than asserting it.

---

## Actors

- **A1 — Engineer / tech-interviewer (primary evaluator).** Clones the repo, runs it, reads code and tests. Disqualifies on a broken clone-and-run. Convinced by hard-problems demonstrably proven (a green zero-oversell-under-load test beats a clicked-through UI). Skims, then drills into whatever the README points at as interesting.
- **A2 — Maintainer (the developer).** Needs the proofs runnable on their own machine to trust the claims and to demo/discuss in an interview. Secondary, but the proofs must work for them too.

---

## Key Flows

### F1 — Engineer evaluation

- **Trigger:** evaluator opens the GitHub repo.
- **Actors:** A1.
- **Steps:** read README → see the four hard-problems named up front with "what's hard / where's the code / which test proves it / how to run it" → pick one → run its proving test → see it green → read the production code it exercises.
- **Outcome:** convinced of real engineering depth in a few targeted minutes, without spelunking a changelog.
- **Covered by:** R3, R4, AE5.

### F2 — Clone-and-run-enough

- **Trigger:** evaluator decides to run it locally.
- **Actors:** A1.
- **Steps:** clone → (Docker running) → `task up` → reach the Aspire dashboard → poke at least one service.
- **Outcome:** "it actually runs" — table-stakes credibility. Full six-service perfection NOT required; "boots enough to explore" is the bar.
- **Covered by:** R6, R7, AE6.
- **Escape path:** if full boot isn't reached, the README documents the Docker + native-Postgres-port prerequisite so the evaluator understands what to expect rather than hitting an unexplained wall.

### F3 — Run-the-proofs

- **Trigger:** evaluator (or maintainer) wants to verify a hard-problem claim.
- **Actors:** A1, A2.
- **Steps:** follow a documented command per proof (or a "run all proofs" entry point) → the scale-gate / property / integration test executes locally against Testcontainers → green.
- **Outcome:** the signature claims verified on the evaluator's own machine, not taken on faith.
- **Covered by:** R1, R2, AE1-AE4.

---

## Requirements

**Proof runnability (the core of "done")**

- R1. Each of the four hard-problems has a proving test that runs green **locally** (Docker + Testcontainers) via a documented command — un-Skip / opt-in mechanism for the relevant `Category=Integration` and `Category=Load` tests, plus a documented entry point to run them. The four: (1) reservation oversell-safety scale gate, (2) noisy-neighbor multi-tenant sync scale gate, (3) cross-tenant isolation, (4) cross-role RBAC denial.
- R2. The `Category=Load` scale-gate tests are confirmed to **actually pass locally** — they have never been run locally (CI-only). Treat as discovery: fix what breaks to make the proof runnable, scoped to runnability, not re-architecture.
- R3. Each hard-problem maps explicitly: proving-test path ↔ the production code path it exercises, so the README routing (R4) and an evaluator can connect proof to implementation.

**README / routing (the artifact the evaluator meets first)**

- R4. README reframed to engineer-orient: lead with the four hard-problems (what's hard, where's the code, which test proves it, how to run that test), an architecture diagram, and concise "how to run" + "how to run the proofs" sections. The sprint-by-sprint changelog moves out of the top into a separate history doc (or a collapsed section).
- R5. README includes a short forward-looking section ("what would you change to serve 100+ tenants" / "what the SOC2 follow-up looks like") — carried from the original Phase-4 ship gate; engineers value the reasoning.

**`task up` (table stakes, de-scoped)**

- R6. `task up` boots **enough for an engineer to explore** — Aspire dashboard up + enough services reachable to poke. The remaining dev-stack issues (pgbouncer config, catalog-vs-app-role ordering, HTTP service startup) are fixed only to the extent they block "boots enough to explore." All-six-services-perfect is explicitly NOT required.
- R7. A documented, accurate "run locally" path covering the Docker prerequisite and the native-Postgres-port coexistence reality, so a stranger's clone-and-run doesn't hit the same blind first-boot wall the maintainer did.

**Multi-channel breadth (scope amendment — makes the headline identity real)**

- R9. A second channel adapter — **Lazada** — is implemented end-to-end against a Lazada mock server, mirroring the Shopee reference adapter's surface (webhook receive + signature verification + stock-update push + product mapping). The build demonstrates the plugin-architecture claim: adding the channel requires no change to the `IChannelAdapter` framework / factory contract beyond the new adapter + its registration.
- R10. A test proves the multi-channel story works: an order/webhook for the Lazada channel is received, mapped, and routed (idempotency + tenant-routing intact), and the sync engine pushes stock to BOTH channels through the same coalescing / rate-limit / priority / breaker pipeline. (Scope: prove a 2nd channel flows through the existing engine; full cross-channel allocation-rebalance-on-quota-exhaustion is a deeper stretch — see Scope Boundaries.)
- R11. Lazada is one of the README-routed hard-problems (R4): the "pluggable channel framework" claim links to the Shopee + Lazada adapters side by side and to the proving test, so an evaluator sees extensibility demonstrated, not asserted.

**Clean repo**

- R8. Code stays navigable and git history coherent (largely already true). Confirm no local-run hacks are committed (the dev-stack coexistence hacks were already reverted; the genuine fixes were committed in `acc2c91`).

---

## Acceptance Examples

- AE1. **Covers R1, R2.** An engineer runs the documented reservation-oversell proof and the scale-gate test passes: 5 tenants × 1,000 concurrent reservations against 1,000 units each → exactly 1,000 successes per tenant, zero oversell, zero cross-tenant successes, per-tenant fairness floor ≥ 0.85.
- AE2. **Covers R1, R2.** An engineer runs the noisy-neighbor proof: tenant A bursts while tenants B-E run normal load; B-E hold their p99 SLO and the fairness floor ≥ 0.85.
- AE3. **Covers R1.** An engineer runs the tenant-isolation proof: a request scoped to one tenant reads only that tenant's DB; a cross-tenant header attempt returns 403 with zero cross-tenant data access.
- AE4. **Covers R1.** An engineer runs the RBAC proof: cross-role denial tests are green (e.g., a Picker JWT against ship-confirm returns 403; the 4-role hand-off drives one saga Picker → Packer → Dispatcher).
- AE5. **Covers R4.** The README's top section names the four hard-problems, each with a link to its code + proving test + run command. The sprint changelog is no longer the first thing a reader encounters.
- AE6. **Covers R6, R7.** With Docker running, a fresh clone runs `task up` and reaches the Aspire dashboard plus at least one pokeable service within ~5 minutes; if any prerequisite (Docker, a free Postgres port) is needed, the README states it up front.
- AE7. **Covers R9.** A Lazada webhook (signed by the Lazada mock) is received, signature-verified, product-mapped, and persisted with `(channel_id, provider_event_id)` idempotency in the correct tenant DB — the same receiver pipeline Shopee uses, with no change to the `IChannelAdapter` contract. A replay of the same Lazada event produces exactly one order.
- AE8. **Covers R10.** A stock change for a SKU sold on both Shopee and Lazada drives a push to BOTH channels through the sync engine (coalescing / rate-limit / priority / breaker), demonstrably exercised by a test — proving the engine is multi-channel, not Shopee-only.

---

## Success Criteria

- **Human outcome.** In ~15 minutes an engineer evaluator can: (a) run `task up` and watch it boot enough to explore, (b) run the four proofs and see them green, (c) read the README and know exactly where the impressive engineering is and why it's hard. They leave convinced of depth — without reading a changelog.
- **Downstream-agent handoff.** `ce-plan` can sequence the work without inventing: which tests are the "proofs," the shape of the README reframe, how far the `task up` repair goes, or the run-the-proofs mechanism. The remaining unknowns are explicitly the discovery items in Outstanding Questions.

---

## Scope Boundaries

- **Demo video — out.** Human-recorded artifact; for an engineer audience the runnable proofs + README replace it.
- **All-six-services-perfect boot / full interactive live demo — out.** `task up` only needs to boot enough to explore (R6).
- **Swagger-per-service — out** of the core finish line (optional later polish).
- **Dev-stack re-architecture beyond "boots enough to explore" — out.** E.g., a perfect PgBouncer transaction-pooling topology in dev; fix only what blocks exploration.
- **Lazada adapter — IN** (scope amendment 2026-05-27): the 2nd channel that makes "multi-channel" honest + demonstrates plugin extensibility. See R9-R11.
- **TikTok adapter — out.** A 3rd adapter is diminishing returns once the 2nd proves the plugin architecture; defer to roadmap completion.
- **Full cross-channel allocation engine (rebalance-on-quota-exhaustion across channels) — out.** R10 proves a 2nd channel flows through the existing sync engine; the rule-weighted allocation/rebalance across channels (original Phase-2 Sprint-5/6 stretch) stays deferred.
- **Analytics module — out.** Roadmap-completion workstream to tackle after the project boots + the proofs are demonstrable.
- **Oversell-compensation flow (original Sprint-6) — out.** The oversell *detection* correctness is proven by the reservation ledger hard-problem; the seller-notified compensation/cancellation flow is deferred with Lazada's deeper scope.
- **Production deployment / real hosting — out.** The finish line is "demonstrable to an engineer who clones it," not "deployed."

---

## Key Decisions

- **Primary evaluator = engineer/tech-interviewer who clones + runs + reads code.** The strictest bar: a broken clone-and-run is disqualifying, so running + code + tests matter most and marketing polish least.
- **"Done" = the four hard-problems demonstrably proven, not feature-completeness or all-services-boot.** The differentiator is engineering depth in specific hard problems, not WMS surface area (which is intentionally incomplete — Analytics/Lazada deferred).
- **Tests-as-proof, not live-demo.** The proving tests (scale gates, property, integration) are the artifact an engineer trusts; the README routes to them. A noisy-neighbor burst can't be shown on a dashboard anyway.
- **Multi-channel must be real, not asserted — via a 2nd adapter (Lazada), not N.** A "multi-channel WMS" with one channel is a hollow headline; the cheapest honest fix is the 2nd adapter, which doubles as proof the plugin architecture works. The 3rd channel (TikTok) and the full cross-channel allocation engine are diminishing returns and stay deferred. This was a scope amendment after the initial finish-line draft deferred all of multi-channel.

---

## Dependencies / Assumptions

- **Docker required locally** for both the proofs (Testcontainers) and `task up`. Assume the evaluator has Docker — state it in the README.
- **The proving tests are Testcontainers-based and independent of the Aspire `task up` stack** (verified: the integration fixtures spin their own `PostgreSqlContainer`; the scale gates likewise). So making the proofs runnable is largely **decoupled** from the dev-stack repair — that's why `task up` can be de-scoped to "boots enough to explore."
- **Assumption (unverified):** the proving tests actually pass when run locally with Docker. They have only ever run in CI; the `Category=Load` scale gates were never run locally at all. This is genuine discovery risk (R2) — the work may surface breakages like the dev-stack boot did.
- **Native Postgres on host port 5432 is a machine-specific reality** on the maintainer's box; the clone-and-run path must account for the Docker-Postgres-port question (the maintainer hit this; a stranger may or may not).

---

## Outstanding Questions

### Deferred to Planning

- **[Affects R1, R2][Needs research]** Do the `Category=Load` scale-gate tests pass locally as-is, or do they need fixture/timing/port adjustments? Never run locally — plan-time discovery.
- **[Affects R6][Technical]** How far does the `task up` repair go for "boots enough to explore" — which specific services must be reachable for the engineer-poke flow? Plan decides after identifying the minimum set.
- **[Affects R1][Technical]** Mechanism for "run the proofs locally" — env-var un-Skip, an xUnit category filter, a dedicated `task` target, or a documented `dotnet test --filter` recipe? Plan decides.
- **[Affects R6][Technical]** Which pgbouncer direction (edoburu vs bitnami-config-fix) best serves "boots enough to explore" + the prod-compose consistency — resolve at plan-time against the actual minimum-boot need.
