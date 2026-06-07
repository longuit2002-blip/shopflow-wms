# Portfolio finish-line — sign-off (2026-06-03)

**Status: COMPLETE.** Tag: `v0.18.0-portfolio-finish-line`. Branch: `feat/portfolio-finish-line` (cut from `v0.17.0-sprint-13`).
Brainstorm: [docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md](../brainstorms/2026-05-27-portfolio-finish-line-requirements.md).
Plan: [docs/plans/2026-05-27-001-feat-portfolio-finish-line-plan.md](../plans/2026-05-27-001-feat-portfolio-finish-line-plan.md).

## What the finish-line set out to do

Take ShopFlow WMS from "22 green sprint tags that had never run on a developer machine" to "an engineer can clone it, run the four hard-problem proofs green locally, and read a README that routes straight to the impressive engineering" — plus make the **multi-channel** headline honest with a real second marketplace adapter (Lazada). "Done" = the four hard-problems demonstrably proven + multi-channel real, **not** feature-completeness, not a full live demo (per the brainstorm's explicit scope).

## The headline result

`task proofs` (Docker live, `SHOPFLOW_RUN_PROOFS=1 dotnet test --filter "Category=Proof"`) runs **green locally**, end-to-end, covering all four hard-problems + the multi-channel proof:

| Proof | Suite | Result |
|---|---|---|
| Oversell-safe reservation ledger | `MultiTenantScaleGateTests` + `ReservationLedgerProperties` | 2 + 5 |
| Noisy-neighbor multi-tenant sync | `MultiTenantStockSyncScaleGateTests` | 1 (+1 R9 breaker chaos deliberately skipped) |
| Database-per-tenant isolation | `CrossTenantRoutingTests` + `AuthCrossTenantTests` + `CrossTenant403Test` | 5 + 6 |
| Cross-role RBAC + 4-role hand-off | `CrossRoleDenialTests` + `HandoffWorkflowTests` | 16 |
| Multi-channel sync (Shopee + Lazada) | `MultiChannelSyncProofTests` | 1 |

**36 proof facts green, 1 documented skip.** All Testcontainers-based and decoupled from the Aspire `task up` stack.

## Units

All 10 units shipped (U0–U4 + the U6 boot fixes landed earlier on-branch; U5, U4-finish, U7, U8, U6-finish, U9, U10 this completion pass):

- **U0** — brainstorm + plan (+ the post-doc-review revision that reclassified the proof estate as two-tier).
- **U1** — proof-run opt-in: `ProofGate` + `[ProofFact]`/`[ProofProperty]` (skip unless `SHOPFLOW_RUN_PROOFS=1` or `CI=true`) + `task proofs` target.
- **U2** — Tier-1 proofs green locally (oversell scale gate, ledger FsCheck, cross-tenant routing); fixed a production `JwtTokenIssuer` Singleton-consumes-Scoped DI bug.
- **U3** — StockSync noisy-neighbor harness (real `FairnessCalculator`); fixed 8 never-ran composition bugs.
- **U4** — cross-role denial harness (14 facts); made `Outbound.Api` boot (double-`AddMassTransit` + missing `ITenantCatalog`).
- **U4-finish** — 4-role hand-off happy-path (`HandoffWorkflowTests`) un-skipped + green over HTTP; the deferred `POST /orders` + `/seed` 500 (`CreatedAtAction` Async-suffix) fixed.
- **U5** — Auth cross-tenant isolation harness: `MultiTenantAuthFixture` (catalog + 2 tenant DBs migrated + seeded) + 6 green proofs.
- **U7** — Lazada channel adapter + parser + signature verifier + DI + mock server + the **K8 channel-agnostic webhook signature** (`ISignatureVerifier.SignatureHeaderName`). `ChannelAdapterFactory` unchanged — plugin-by-construction. Channel.UnitTests 139, Channel.IntegrationTests 13.
- **U8** — multi-channel sync proof: one stock change → push to Shopee **and** Lazada through the engine.
- **U6-finish** — config-driven Postgres host port (`DevStack:PostgresHostPort`) for native-5432 coexistence; remaining K6-floor work (edoburu PgBouncer swap, Inventory.Api/Gateway wiring) documented as a clean-clone/CI repair per AE6.
- **U9** — README reframed to route the four hard-problems (code ↔ proving-test) + the multi-channel story; sprint changelog moved to `docs/sprint-history.md`.
- **U10** — this sign-off + the `Category=Proof` trait fix + `.gitignore` node_modules + CLAUDE.md current-stage update + tag.

## Key decisions & deviations (this completion pass)

- **U7 was built by a worktree-isolated subagent on a stale `main` base** (Sprint-5-era + 2 agents-md commits, 156 commits behind the finish-line branch). Rather than merge the divergence (which would have pulled unrelated docs commits + reverted finish-line work), the feature commit `7d85ccf` was **cherry-picked** onto the branch; 3 conflicts resolved by keeping the branch's already-better fixes (TenantCatalog `Size=1` from U3; the Polly v8 predicate from Sprint-8.5; the U6 `AddChannelAdapterFramework` extraction). One never-run gap the subagent couldn't see on its stale base — `TenantWebhookHarness` missing the Sprint-9 KTD7 `Auth:ForwardedHeaders:KnownNetworks` setting — was then fixed; Channel.IntegrationTests 13/13.
- **U8 run-first surfaced two harness gaps** the `StockSyncHappyPathTests` template carried (it predates the Sprint-9 guard + was never run locally): the KTD7 ForwardedHeaders allowlist, and that WAF config **must** go through `builder.UseSetting`, not `ConfigureAppConfiguration` — the latter merges after `AddControlPlane` reads it at `builder.Build()`, so the appsettings-default ControlPlane connection won and the dispatcher hung connecting to an unreachable DB. Both aligned to the proven U3 `BuildHost` pattern.
- **U6-finish honored verification-before-completion**: a full live `task up` boot to the K6 floor is not reproducible on this dev machine (native Postgres on 5432) and the edoburu swap + Inventory.Api/Gateway wiring need live boot verification. Per the brainstorm's AE6 ("state the prerequisite up front") + K6 ("document which and why"), these are documented as the clean-clone/CI dev-stack repair rather than shipped unverified. The config-driven port (the one safe, build-verified fix) landed.
- **U10 caught a tagging gap**: `[ProofFact]` sets only the conditional Skip — it does not add `[Trait("Category", "Proof")]`. The U5 Auth tests had `[ProofFact]` + `Category=Integration` but were missing `Category=Proof`, so `task proofs` skipped them. Added the trait to both Auth cross-tenant classes; they now run + pass (6/6) under the gate.

## Verification gates

- `dotnet build ShopFlow.sln` → **0 errors / 0 warnings**.
- `task proofs` (Docker live) → **36 proof facts green, 1 documented skip** (see table above).
- Backend unit suite + module integration suites carry forward green (Channel.UnitTests 139; Outbound/StockSync/Auth integration proof suites green under the gate).
- Clean repo: `node_modules/` gitignored; no committed local-run hacks (the 5432/PgBouncer-bypass workarounds remain uncommitted config divergences per the dev-stack note).

## Trade-offs carried forward (the documented dev-stack repair + roadmap completion)

These were deliberately scoped out per the brainstorm and are NOT finish-line blockers:

1. **`task up` to the full K6 floor** — edoburu PgBouncer swap (needs live scram/userlist verification), Inventory.Api composition completion (`AddControlPlane` + `/health`) + AppHost resource wiring, Gateway YARP service-discovery wiring. A clean-clone/CI boot job. See [the first-boot note](../solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md).
2. **TikTok / Shopify adapters** + full cross-channel allocation/rebalance + per-SKU channel mapping → a "multi-channel completion" workstream.
3. **Analytics module** (currently 501) + Inbound PO handlers + the ComingSoon frontend pages → commercial-grade scope, out of portfolio scope by design.
4. **Frontend Packer/Dispatcher UI** + the Sprint-13 hardening list (non-Owner MFA, force-change-on-first-login, `auth_audit_log` write-path on Outbound) → unchanged from the Sprint-13 sign-off.

## Next step

The portfolio is demonstrable: `task proofs` is green, the README routes to the engineering, and multi-channel is real. The remaining work is either the documented clean-clone/CI dev-stack repair (to make `task up` reach the full K6 floor) or roadmap-completion / commercial-grade features — both explicitly outside the portfolio finish-line.
