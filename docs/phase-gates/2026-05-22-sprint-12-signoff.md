# Sprint-12 sign-off — Second non-Owner role (Dispatcher) + 3-role hand-off proof

**Date**: 2026-05-22
**Tag**: `v0.16.0-sprint-12`
**Branch**: `feat/sprint-12-second-non-owner-role` (cut from `v0.15.0-sprint-11`)
**Plan**: [`docs/plans/2026-05-22-004-feat-sprint-12-second-non-owner-role-plan.md`](../plans/2026-05-22-004-feat-sprint-12-second-non-owner-role-plan.md)
**Brainstorm**: [`docs/brainstorms/2026-05-22-sprint-12-second-non-owner-role-requirements.md`](../brainstorms/2026-05-22-sprint-12-second-non-owner-role-requirements.md)

---

## Summary

Sprint-12 lands the **Dispatcher role end-to-end** and proves the Sprint-9.5 + Sprint-10 + Sprint-10.5 + Sprint-11 defense-in-depth stack handles a **3-role hand-off workflow** on one order's lifecycle (`Picker confirms pick → Owner confirms pack → Dispatcher confirms ship`) with **cross-role denial paths** pinned at each transition.

Single Dispatcher role baseline pre-seeded via `RolePermissionsSeed` extension (Sprint-11 U1) with **3 keys**: `outbound.orders.read` + `outbound.orders.ship-confirm` + `hub.connect`. Same KTD1 ADDITIVE-ONLY contract as Picker: re-running provisioning preserves Owner-added Dispatcher keys via `ON CONFLICT (role, permission_key) DO NOTHING`, but Owner *deletions* from the baseline REVERT on next provision run.

**1 new pick-action button** ships on the order-detail surface (`/orders/$orderId`) — `ConfirmShip` — gated by `usePerm('outbound.orders.ship-confirm')` reactive subscription AND `detail.status === 'AwaitingShip'` (Order aggregate field, NOT saga `currentSagaState` — KTD2 doc-review correction). Carrier tracking number surfaces in success toast AND on a persistent `<Pill kind="ok">` in the order-detail header post-ship (KTD10).

Mutation shares the existing `createIdempotentMutation<TReq, TRes>` factor (Sprint-11 KTD3) — 4th consumer alongside Sprint-7's `seedOrder` + Sprint-11's `confirmPick` + `markPickFailed`. Sprint-7 + Sprint-11 behavior unchanged.

**E2E Docker-backed happy-path test** at `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` drives the 3-role hand-off through one saga instance. **Docker-backed cross-role denial test** at `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` exercises 6 negative paths (4 baseline + 2 doc-review-mandated: ordering-pin proving perm-gate fires before state-gate, and union-of-perms behavioral pin documenting the KTD1 additive-only contract's consequence). Both files Skip-marked locally; CI Docker tier runs the full unskipped suite.

**Doc-review pipeline executed before code landed**: 1 P0 architectural finding at cross-persona agreement confidence 100 (KTD2 saga-state-vs-Order.Status conflation — `OrderDetailDto.currentSagaState` is sourced from `saga_state.CurrentState` which never reaches `'AwaitingShip'`; the Order aggregate's `status` field does) resolved in-plan via Option A (gate U3 on `detail.status`, separately verify mid-flow saga state via GET response). 7 P1 fixes applied in-plan + 1 safe_auto fix. 5 advisory observations carried forward to Sprint-12.5 / Sprint-13 polish.

## Verification gates

All gates met.

### Build
```
dotnet build ShopFlow.sln → 0 errors / 0 warnings across 47 projects
```

### Unit tests
- `tests/ShopFlow.Migrate.UnitTests/` → **61 passed** (was 52 Sprint-11 baseline; +9 new Dispatcher facts including the security-F1 `PickerBaseline_DoesNotContain_OutboundOrdersShipConfirm` baseline-isolation guard)
- Sprint-11 baseline tests across Auth.UnitTests + Inventory.UnitTests + Outbound.UnitTests + Inbound.UnitTests + SharedKernel.UnitTests preserved unchanged

### Integration tests (CI Docker tier — Skip-marked locally)
- `tests/ShopFlow.Migrate.IntegrationTests/Provisioning/RolePermissionsSeedIntegrationTests.cs` → 4 Sprint-11 scenarios + 2 new Sprint-12 scenarios (fresh-tenant 31-row provision + AE6 operator-pre-grant scenario)
- `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` → 1 Fact (happy-path 3-role hand-off)
- `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` → 6 Facts (4 baseline + ordering pin + union-of-perms behavioral pin)
- All 7 Handoff facts Skip-marked locally per Sprint-1+ posture

### Frontend
- Vitest **549 passing** (Sprint-11 baseline 474 + 7 new `confirmShip` mutation tests + 10 new ship-action visibility/tracking-pill/a11y tests + Sprint-11 misc carried forward) / 4 pre-existing Sprint-7 baseline failures unchanged
- `useOrderMutations.test.tsx` → 24 passed / 1 pre-existing failure (Sprint-7 "Body is unusable" baseline; documented in CLAUDE.md)
- `$orderId.test.tsx` → 18 passed (8 Sprint-11 + 10 new Sprint-12)

## Units shipped

| Unit | Commit | Description |
|---|---|---|
| **U0** | `3afc823` | Branch cut + brainstorm + plan + 10 KTDs in opening commit body |
| **U1** | `7822c10` | `RolePermissionsSeed.DispatcherBaseline` 3-key + 9 new unit facts + 2 new integration scenarios |
| **U2** | `47417f1` | Frontend `dispatcherBaseline.ts` + `ordersApi.confirmShip` + `useOrderMutations.confirmShip` (4th factor consumer) + 7 new mutation facts |
| **U3** | `1098307` | ConfirmShip button on order-detail + tracking-pill in header + 10 new per-component facts (7 visibility + 2 tracking-pill + 1 axe smoke) |
| **U4** | `467354e` | `HandoffFixture` + `HandoffWorkflowTests` (1 Skip-marked happy-path Fact) + Compile-link to `RolePermissionsSeed` |
| **U5** | `4e11d03` | `CrossRoleDenialTests` 6 Skip-marked Facts (4 baseline + adversarial-F3 ordering pin + adversarial-F8 union-of-perms behavioral pin) |
| **U6** | this commit | Sign-off + Auth AGENTS.md + README + CLAUDE.md + CHANGELOG + tag `v0.16.0-sprint-12` |

## Key Technical Decisions (10)

1. **KTD1** — ADDITIVE-ONLY re-seed contract inherited from Sprint-11. `ON CONFLICT (role, permission_key) DO NOTHING` preserves Owner additions across re-seed but Owner *deletions* from the Dispatcher baseline REVERT on next provision run. Documented in Auth AGENTS.md as load-bearing for both Picker AND Dispatcher.
2. **KTD2** — UI gate reads `Order.Status === 'AwaitingShip'` (Order aggregate field which DOES reach `AwaitingShip` via `MarkAwaitingShip()`), NOT saga `currentSagaState` (which has no `AwaitingShip` handler — `FulfillmentSaga.cs:213` TODO documents the missing auto-transition). Doc-review caught this conflation at cross-persona agreement confidence 100; Option A resolution applied to U3 + U4 + AE2 + AE5 + R7.
3. **KTD3** — Reuse Sprint-11's `createIdempotentMutation<TReq, TRes>` factor. `confirmShip` is the 4th consumer alongside `seedOrder` + `confirmPick` + `markPickFailed`. Zero changes to the factor signature; Sprint-7 + Sprint-11 behavior preserved.
4. **KTD4** — Parallel `HandoffFixture` instead of generalizing Sprint-11's `PickerFixture`. Lower regression risk; ~85% conceptual duplication carrying-cost listed in "Deferred to Follow-Up Work" for Sprint-13+ consolidation.
5. **KTD5** — Factory-form `MockShippingProvider` override via `ConfigureTestServices`: `RemoveAll<IMockShippingProvider>` + `AddSingleton(sp => MockShippingProvider.WithFlakeRate(sp.GetRequiredService<ResiliencePipeline>(), 0.0))`. Doc-review feasibility-F2 corrected the original instance-form snippet that had no `pipeline` variable in scope.
6. **KTD6** — Test namespace `.Handoff` (no `.PickerE2E`-style suffix needed — `Handoff` doesn't shadow any Outbound.Domain type).
7. **KTD7** — 30s wall-time budget for the 3-transition hand-off (3 × 10s per-transition polls). InMemory transport bus-readiness wait via `Factory.CreateClient()` warm-up at end of `InitializeAsync` mitigates the adversarial-F4 startup-race flake mode. Per-transition wall-time logging via `HandoffWatch` for CI flake investigation evidence.
8. **KTD8** — Minor version bump `v0.16.0-sprint-12` matching Sprint-9.5 + Sprint-11 precedent for net-new feature surface work.
9. **KTD9** — No dedicated `dispatcherBaseline.ts` ↔ backend `DispatcherBaseline` reflection contract test. Per-component coverage at U3 catches drift cheaply.
10. **KTD10** — `ConfirmShipResponse` shape (`labelUrl + trackingNumber + nested OrderResponse`) + persistent tracking-pill on order-detail header rendered when `detail.trackingNumber !== null && detail.labelUrl !== null`. Toast as primary success surface; tracking-pill as post-dismiss fallback. Uses existing nullable `OrderDetailDto` fields (no new DTO).

## Doc-review summary

5 reviewers dispatched (coherence + feasibility + design-lens + security-lens + adversarial). Results:

**Critical (P0 — manual revision required before code landed):**
- **adversarial-F1 + feasibility-F1** (cross-persona agreement confidence 100 each): KTD2 / U3 / U4 conflated `Order.Status` with saga `CurrentState`. **Resolved via Option A in-plan**: gate U3 on `detail.status === 'AwaitingShip'` + poll saga for `'Packed'` mid-flow + separately verify `Order.Status === 'AwaitingShip'` via GET response.

**Important (P1 — fixes applied in-plan before code):**
- **feasibility-F2**: KTD5 instance-form override snippet rewritten to factory-form
- **adversarial-F3**: Added perm-before-state ordering test in U5 (`Dispatcher_AttemptsPickConfirm_OnAwaitingShipOrder_Returns403_NotStateError`)
- **adversarial-F4**: KTD7 bus-readiness wait added to fixture
- **adversarial-F8**: Added union-of-perms behavioral pin in U5 (`PickerWithManualShipConfirmGrant_CanShip_BehavioralPin`)
- **security-F1**: Added `PickerBaseline_DoesNotContain_OutboundOrdersShipConfirm` guard in U1
- **security-F4**: AGENTS.md MFA-deployment guidance + R-5 impact upgrade Medium→High
- **design-F1 + design-F2 + design-F3 + design-F4**: ConfirmShip error-recovery behavior + persistent tracking-pill + single-click rationale + axe smoke test all added to U3

**Safe-auto (applied silently):**
- **feasibility-F3**: U1 `InsertAsync` 5-arg signature corrected in plan body

**Advisory (P2 — observations recorded, no code changes):**
- adversarial-F5: saga seed bypass may need reservation rows — verify at U4 execution time; documented as plan U-decision
- adversarial-F6: KTD5 zero-flake means CI never exercises Polly retry — gap documented, lower-tier `MockShippingProviderTests` cover retry
- adversarial-F7: parallel-fixture drift risk — listed in "Deferred to Follow-Up Work"
- design-F5: transient `Packed` UI gap state — narrow race window, no test added
- security-F2/F3/F5: UI-gate-not-security-boundary language, cross-state denial coverage gap, JWT-builder drift potential — all advisory, no code changes

## Deviations from plan

1. **`IBusControl.WaitUntilStarted` not used** — that extension doesn't exist in the installed MassTransit version. Substituted `Factory.CreateClient()` warm-up which triggers the WAF `IHostedService` pipeline (InMemory transport starts the bus synchronously through that path). Comment in `HandoffFixture.cs` flags `IBusHealth` as the right shape for RabbitMQ-backed CI tier.
2. **`Dispatcher_AttemptsPickConfirm_Returns403` + `Dispatcher_AttemptsPackConfirm_Returns403`** — the plan also listed `Dispatcher_AttemptsPickConfirm_OnAwaitingShipOrder` as a 5th adversarial-F3 fact. All 3 facts shipped; the plan's "6 facts" count is intact (4 baseline + adversarial-F3 + adversarial-F8 = 6).
3. **Compile-link path used for `RolePermissionsSeed.cs`** — plan named `Migrate` ProjectReference as an option; the executable `OutputType=Exe` would collide with Outbound.Api `Program` (Sprint-10.5 U4 precedent). Compile-link is the cleaner path.

## Trade-offs carried forward to Sprint-12.5+

1. **MarkShipFailed failure path + saga compensation** → Sprint-12.5 or later. The reason-modal pattern Sprint-11 proved doesn't need re-proving; new saga events + handlers + compensation transitions required.
2. **`auth_audit_log` write-path wiring on `ConfirmShipHandler`** → Sprint-11.5 / Sprint-12 follow-up workstream. Sprint-9 ships `IAuthAuditLogRepository` storage layer but no command handler calls `AppendAsync`.
3. **Picker / Dispatcher MFA enforcement** → Sprint-13+ hardening decision. Owner is `MfaRequired=true` by R17; non-Owner defaults stay `false`. Plan documents production-deployment guidance in Auth AGENTS.md: operators SHOULD set `users.mfa_required = true` on Dispatcher accounts pending Sprint-13 engine-level enforcement.
4. **Force-change-on-first-login enforcement** → future production hardening.
5. **Packer as a fourth role** → out of scope; Pack stays Owner-only at Sprint-12.
6. **Dispatcher-specific UI views** ("My ship queue") → future sprint when product surface justifies.
7. **Observability dashboards for per-role denial rates per tenant** → Phase-3 polish.
8. **One-time migration to revoke overlapping keys from Picker** → KTD1 contract holds; operator-runbook audit is the canonical mitigation.
9. **Generalize `PickerFixture` ↔ `HandoffFixture`** (KTD4 deferred work) → Sprint-13+ candidate if duplication surfaces real maintenance pain.
10. **`dispatcherBaseline.ts` ↔ backend `DispatcherBaseline` contract test** (KTD9 deferred) → revisit if drift surfaces.
11. **Tier-3 E2E with `FlakeRate > 0` exercising Polly retry through HTTP** (adversarial-F6) → Sprint-12.5 polish if production gap surfaces.
12. **IBusHealth-based readiness for RabbitMQ-backed CI tier** → KTD7 InMemory warm-up sufficient at Sprint-12.
13. **Secondary `outbound_saga_transitions` row-shape assertion in U4** → optional U-decision deferred per "fold-in if < 30 lines" gate.

## Next implementation step

Cut a fresh branch from `v0.16.0-sprint-12` and start one of:

- **Sprint-12.5 polish** — `auth_audit_log` write-path wiring on `ConfirmPickHandler` + `MarkPickFailedHandler` + `ConfirmShipHandler` + Picker/Dispatcher MFA decision + force-change-on-first-login + `MarkShipFailed` failure path with saga compensation + tier-3 carrier-retry E2E
- **Sprint-13** — Fourth role (Packer) — Pack-confirm moves off Owner; new `Packer` enum value + DB CHECK migration + third non-Owner baseline + 4-role hand-off proof
- **Phase-3 polish** — observability dashboards (per-role denial rates + saga transition latency + notification queue depth) + KMS/Vault TOTP KEK migration + `CREATE INDEX CONCURRENTLY` review + `auth_audit_log` partitioning
- **Dispatcher-specific UI** — `/orders/queue/awaiting-ship` filtered list view for Dispatcher operations
