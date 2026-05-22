---
title: Sprint-12 — Second non-Owner role (Dispatcher) + 3-role hand-off proof
created: 2026-05-22
status: ready-for-planning
origin: solo-brainstorm
actors: [Owner, Picker, Dispatcher]
flows: [F1-pick-pack-ship-handoff, F2-dispatcher-confirm-ship, F3-cross-role-denial]
---

# Sprint-12 — Second non-Owner role (Dispatcher) + 3-role hand-off proof

## Problem frame

Sprint-11 shipped the first non-Owner role (Picker) and proved the Sprint-9.5 / Sprint-10 / Sprint-10.5 defense-in-depth stack works end-to-end under a narrowed `perm[]` claim set with one role. The stack is now correct-for-1-role, but no test exercises it under more than one non-Owner role at a time, and no test drives one saga instance through multiple role-owned transitions. Two failure surfaces remain unproved:

1. **Role-confusion bugs.** Today's negative-path coverage (Sprint-10.5 33+1 Docker 403 tests) narrows Owner by stripping one key. It does not exercise a real Picker JWT against a Dispatcher endpoint, nor a real Dispatcher JWT against a Picker endpoint. Bugs where two non-Owner roles' `perm[]` checks interfere with each other would not surface today.
2. **Saga-state-ownership across role hand-off.** Today's Picker E2E test drives one saga from `AwaitingPick` to `Picked` under one JWT. No test drives a single saga instance through multiple transitions owned by different roles.

Sprint-12 closes both gaps by adding the Dispatcher role (`UserRole.Dispatcher` already exists in the domain + DB CHECK constraint from Sprint-9) with a 3-key `perm[]` baseline pre-seeded via the same `RolePermissionsSeed` extension Sprint-11 used, then proves the stack via a 3-role hand-off E2E test on one order.

## Goal

Prove the defense-in-depth stack handles a 3-role hand-off workflow on one order's lifecycle — `Picker confirms pick → Owner confirms pack → Dispatcher confirms ship` — with cross-role denial paths pinned at each transition.

## Actors

- **A1 — Owner.** Already established (Sprint-1 through Sprint-11). Owns the 24-key `PermissionKeys.All` superset including `auth.admin.*` (9 OwnerCritical keys), all Inbound/Inventory/Outbound/Channel writes including `outbound.orders.pack-confirm`. Continues to be `MfaRequired=true` per Sprint-9 R17.
- **A2 — Picker.** Established in Sprint-11. 4-key baseline: `outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`.
- **A3 — Dispatcher (new in Sprint-12).** 3-key baseline: `outbound.orders.read` + `outbound.orders.ship-confirm` + `hub.connect`. MFA NOT enforced at Sprint-12 (consistent with Picker; hardening decision deferred to a future sprint).

## Key Flows

### F1 — Pick → Pack → Ship hand-off on one order

One order moves through three saga transitions, each performed by a different role's JWT:

1. **Picker** (with Sprint-11 baseline) issues `POST /api/outbound/orders/{id}/confirm-pick` → saga transitions `AwaitingPick → Picked`.
2. **Owner** (with full key set including `outbound.orders.pack-confirm`) issues `POST /api/outbound/orders/{id}/confirm-pack` → saga transitions `Picked → Packed`.
3. **Dispatcher** (with Sprint-12 baseline) issues `POST /api/outbound/orders/{id}/confirm-ship` → saga transitions `Packed → Shipped`.

All three transitions land on the same `saga_state` row (same `CorrelationId`). No role inherits another role's permissions implicitly. `hub.connect` lets each role's SignalR client subscribe to saga state updates for orders they can read.

### F2 — Dispatcher confirms ship from the UI

The order-detail surface (`/orders/$orderId`) gains a single `ConfirmShip` button when:
- Order state is `Packed` (saga is `AwaitingShip` or equivalent), AND
- Active session's `perm[]` contains `outbound.orders.ship-confirm` (verified via `usePerm` reactive subscription).

Button is hidden (not disabled-with-tooltip) when the perm gate fails — consistent with Sprint-11 KTD2 + Sprint-10.5 KTD8 hidden-by-default pattern. No new modal or reason input; ConfirmShip is a direct submission without a failure-path button at Sprint-12.

### F3 — Cross-role denial at every transition

- Picker JWT against `POST /api/outbound/orders/{id}/confirm-ship` → 403 `auth.forbidden`.
- Picker JWT against `POST /api/outbound/orders/{id}/confirm-pack` → 403 `auth.forbidden`.
- Dispatcher JWT against `POST /api/outbound/orders/{id}/confirm-pick` → 403 `auth.forbidden`.
- Dispatcher JWT against `POST /api/outbound/orders/{id}/confirm-pack` → 403 `auth.forbidden`.

These denials prove the per-action `[Authorize(Policy = ...)]` gates from Sprint-10 reject real non-Owner JWTs missing the specific key, not just narrowed Owner JWTs.

## Acceptance Examples

- **AE1** — A fresh tenant provisioned by `shopflow-migrate provision --tenant=<slug>` has exactly 31 `role_permissions` rows: 24 Owner + 4 Picker + 3 Dispatcher. The Dispatcher row set is exactly `{outbound.orders.read, outbound.orders.ship-confirm, hub.connect}`.
- **AE2** — An order seeded directly to state `AwaitingPick`, then driven through `confirm-pick` (Picker JWT) → `confirm-pack` (Owner JWT) → `confirm-ship` (Dispatcher JWT), ends at saga state `Shipped` within a baked-in 30-second timeout. Each transition returns HTTP 200.
- **AE3** — A Picker JWT issuing `POST /api/outbound/orders/{id}/confirm-ship` returns HTTP 403 with problem-details `errorCode: "auth.forbidden"`. Order saga state is unchanged.
- **AE4** — A Dispatcher JWT issuing `POST /api/outbound/orders/{id}/confirm-pick` returns HTTP 403 with problem-details `errorCode: "auth.forbidden"`. Order saga state is unchanged.
- **AE5** — A logged-in Dispatcher session sees the `ConfirmShip` button on `/orders/$orderId` when the order state is `Packed`. A logged-in Picker session on the same order at the same state does NOT see the button. Verified by per-component Vitest render assertion.
- **AE6** — An Owner who manually grants `outbound.orders.ship-confirm` to the Picker role via `/admin/role-permissions` (pre-Sprint-12 deploy), then deploys Sprint-12 and re-runs `shopflow-migrate provision`, ends with BOTH Picker AND Dispatcher rows holding `outbound.orders.ship-confirm`. The Owner's manual grant on Picker is NOT removed by Sprint-12's provisioning (KTD1 additive-only contract preserved from Sprint-11).
- **AE7** — `tests/ShopFlow.Migrate.UnitTests/Provisioning/RolePermissionsSeedTests.cs` (Sprint-11 baseline) gains new facts pinning the `DispatcherBaseline` static readonly list contents (3 keys, named explicitly).

## Requirements

- **R1** — Add `DispatcherBaseline` static readonly list to `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` with the 3 canonical `PermissionKeys` constants: `OutboundOrdersRead`, `OutboundOrdersShipConfirm`, `HubConnect`. Shared `InsertAsync` helper (from Sprint-11) is reused for the Dispatcher loop.
- **R2** — `shopflow-migrate provision` and `shopflow-migrate seed-owner` both extend to write the Dispatcher baseline rows alongside Owner + Picker. Idempotent re-runs (`ON CONFLICT (role, permission_key) DO NOTHING`) preserve Owner additions across re-seed; Owner deletions from the Dispatcher baseline REVERT on the next provision run. Same KTD1 contract as Sprint-11.
- **R3** — `web/src/lib/auth/dispatcherBaseline.ts` exports `DISPATCHER_BASELINE_PERMS: readonly string[]` with the 3 canonical strings. Pattern mirrors Sprint-11's `pickerBaseline.ts`.
- **R4** — Order-detail route (`web/src/routes/_auth/orders/$orderId.tsx`) gains a `ConfirmShip` button inside the existing `order-detail-pick-actions` section (or a new `order-detail-ship-actions` section — planner decision). Button is gated by `usePerm('outbound.orders.ship-confirm')` reactive subscription and additionally conditioned on order state being `Packed`. Button calls a new `useOrderMutations.confirmShip` mutation that consumes the shared `createIdempotentMutation<TReq, TRes>` factor (Sprint-11 KTD3). No new modal; direct submission.
- **R5** — `useOrderMutations.ts` gains a fourth consumer of the shared factor: `confirmShip` mutation hitting `POST /api/outbound/orders/{id}/confirm-ship`. Existing 3 consumers (`seedOrder` Sprint-7, `confirmPick` Sprint-11, `markPickFailed` Sprint-11) preserved unchanged. Sprint-7 + Sprint-11 behavior unchanged.
- **R6** — Per-component Vitest tests cover: Dispatcher session sees ConfirmShip button at Packed state; Picker session does NOT see ConfirmShip button at Packed state; Owner session sees ConfirmShip button at Packed state; non-Packed states hide the button regardless of perm[].
- **R7** — Docker-backed E2E test at `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` drives the 3-role hand-off on one order. Fixture extends or replaces Sprint-11's `PickerFixture` to issue 3 JWTs (Picker / Owner / Dispatcher) via the existing `NarrowedJwtBuilder` (Sprint-10.5 U4 MSBuild Compile-link pattern — KTD4 from Sprint-11 holds). Saga seeded directly to `AwaitingPick` via DbContext writes (Sprint-11 U3 precedent). Test asserts each of the 3 transitions returns 200 AND the saga state poll converges within 30 seconds (Sprint-11 KTD5 timeout pattern, expanded for 3 transitions). Skip-marked locally; CI runs the full Docker-backed suite.
- **R8** — Docker-backed cross-role denial test at `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` exercises the 4 denial paths in F3 above. Each fact issues a real non-Owner JWT (not a narrowed Owner) and asserts HTTP 403 + saga state unchanged. Skip-marked locally; CI runs in Docker tier.
- **R9** — `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings across all projects (same gate as every prior sprint).
- **R10** — All Sprint-11 baseline tests carry forward unchanged: `tests/ShopFlow.Migrate.UnitTests/` 52 passing baseline + new `DispatcherBaseline` facts (target ~55 total); `tests/ShopFlow.Migrate.IntegrationTests/Provisioning/RolePermissionsSeedIntegrationTests.cs` 4 Sprint-11 scenarios pass + 1 new Sprint-12 scenario asserting 31-row fresh-tenant provision (24 Owner + 4 Picker + 3 Dispatcher). Vitest 474 passing Sprint-11 baseline preserved + new ConfirmShip per-component coverage.
- **R11** — `src/Services/Auth/AGENTS.md` gains one line under the existing Sprint-11 Picker baseline note documenting the Dispatcher baseline (3 keys) and reaffirming the additive-only KTD1 contract.

## Scope Boundaries

### Deferred for later

- **MarkShipFailed failure path + saga compensation.** Sprint-12 ships ConfirmShip only. The reason-modal pattern Sprint-11 proved via MarkPickFailedModal doesn't need re-proving; a failure path for ship would require new saga events + handlers + compensation transitions. Lands in Sprint-12.5 or later if operations needs it.
- **`auth_audit_log` write-path wiring on `ConfirmShipHandler`.** Sprint-11 deferred this for all pick handlers; Sprint-12 inherits the same trade-off. `IAuthAuditLogRepository.AppendAsync` is not called by any handler today. Hardening lands in Sprint-11.5 / Sprint-12 follow-up as a separate workstream (not Sprint-12 itself).
- **Picker MFA enforcement.** Owner is `MfaRequired=true` by R17; Picker and Dispatcher are NOT enforced at Sprint-12. Hardening decision deferred.
- **Force-change-on-first-login enforcement.** Future production hardening; not Sprint-12.
- **Packer as a fourth role.** Pack stays Owner-only at Sprint-12. Introducing a Packer role would require a `UserRole` enum value + DB CHECK constraint migration + a third non-Owner baseline + a 4-role hand-off proof. Out of Sprint-12 scope.
- **Dispatcher-specific UI views.** No "My ship queue" filtered list, no Dispatcher-only routes. Existing `/orders` list page renders with the gated ConfirmShip button on each row's detail. Lands in a future sprint when operations surface justifies.
- **Observability dashboards for per-role denial rates per tenant.** Phase-3 polish carries forward.
- **One-time migration to revoke overlapping keys from Picker.** KTD1 additive-only contract is the canonical re-seed semantic. Sprint-12 documents an operator-runbook audit step in CHANGELOG ("audit `/admin/role-permissions` before deploying Sprint-12 if any non-Owner role currently holds `outbound.orders.ship-confirm`"). No migration ships.

### Outside this product's identity

- A general role-permission management UI for *creating new roles at runtime*. Roles are a closed enum (`UserRole.Owner | Picker | Dispatcher`); adding a 4th role is a code change + DB CHECK migration, never an admin-portal action.
- A general permission-grant audit trail accessible from the admin UI. The infrastructure (`IAuthAuditLogRepository`) exists but is not wired and not exposed in the UI; remains deferred.

## Dependencies

- **Sprint-11 must be tagged + deployed** (`v0.15.0-sprint-11`) before cutting Sprint-12. Sprint-12 extends Sprint-11's `RolePermissionsSeed` + frontend hook patterns + saga-state-seeding test fixture; cutting Sprint-12 without Sprint-11 leaves dangling references.
- **`UserRole.Dispatcher` enum value** is already in `src/Services/Auth/ShopFlow.Auth.Domain/UserRole.cs` (verified during brainstorm). DB CHECK constraint already includes `'Dispatcher'`. No domain migration needed.
- **`PermissionKeys.OutboundOrdersShipConfirm`** is already in `PermissionKeys.All` (Sprint-10 KTD8 canonical mapping table). No catalog migration needed.
- **Saga `Picked → Packed → Shipped` transitions** are already wired (Sprint-3-redux). Both `OrdersController.PackConfirmAsync` and `OrdersController.ShipConfirmAsync` already carry their per-action `[Authorize(Policy = ...)]` attributes (Sprint-10 U2 work). No backend code needs to change for the gate to fire correctly under a Dispatcher JWT.
- **Sprint-10.5 `NarrowedJwtBuilder` (MSBuild Compile-link)** carries forward as the JWT-mint mechanism for the new tests, mirroring Sprint-11 KTD4. No new shared infrastructure needed.

## Assumptions

- **No tenant in the wild has manually granted `outbound.orders.ship-confirm` to Picker.** If a tenant has, Sprint-12 will leave that grant in place (KTD1 contract). Documented as operator-runbook step.
- **The 30-second baked-in timeout for the 3-transition hand-off is sufficient.** Sprint-11's 10s timeout was for a single transition; the 3-transition test gets 30s headroom. If CI Docker tier proves flaky, the timeout can be raised at execution time as a deviation rather than re-brainstorming.
- **The Sprint-11 `PickerFixture` can be extended in place** (e.g., to issue 3 JWTs and seed past `Packed` for some scenarios) without breaking existing Sprint-11 happy-path tests. If the extension creates unacceptable coupling, the plan can fork to a new fixture.
- **No frontend `MarkShipFailed` UX is needed at Sprint-12.** Operations can rely on Owner-tier manual intervention for ship failures until a real failure-path UI lands.

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **R-1** Hand-off E2E test flakes in CI due to saga state polling timing | Medium | Medium | 30s baked-in timeout (R7); Sprint-11's 10s timeout for 1 transition expanded proportionally. Polling interval kept at 500ms. If flake-rate exceeds 1-in-20, raise timeout to 60s as a deviation rather than re-architecting. |
| **R-2** Cross-role denial test for Dispatcher→pick reveals a real bug where the policy gate accepts the JWT | Low | High | This is the test's *purpose*. If it fires, that's a Sprint-10 regression and Sprint-12 catches it before any tenant deploys. Mitigation IS the test. |
| **R-3** Sprint-12 introduces a 4th consumer of `createIdempotentMutation` factor and breaks Sprint-7 / Sprint-11 behavior | Low | High | Per-component Vitest regression coverage on `confirmShip` mirrors Sprint-11 patterns for `confirmPick` + `markPickFailed`. R10 explicitly pins Sprint-11's 474-passing baseline. Refactor is additive (one new consumer, no changes to existing). |
| **R-4** Tenant in the wild has Picker manually granted `outbound.orders.ship-confirm`, post-Sprint-12 Picker can now ship orders without operator awareness | Low | Medium | KTD1 contract is documented behavior. Operator-runbook audit step in CHANGELOG. Sprint-13+ could add a UI lint to RolePermissionsEditor flagging cross-role overlaps. |
| **R-5** Dispatcher MFA absence becomes a real security concern once tenants start provisioning Dispatchers at scale | Medium | Medium | Consistent with Picker decision. Owner is `MfaRequired=true` (R17); non-Owner MFA is a separate hardening decision. Sprint-12 documents as known deferral; production tenants can manually toggle `mfa_required` on Dispatcher users via Owner admin surface if needed. |
| **R-6** The new ConfirmShip button breaks the existing OrdersTable / order-detail Vitest a11y harness due to layout drift | Low | Low | Per-component test coverage at R6 catches obvious regressions; full a11y harness extension is deferred to a Sprint-11.5-style polish workstream per existing carry-forward. |
| **R-7** Frontend `dispatcherBaseline.ts` drifts from backend `RolePermissionsSeed.cs` `DispatcherBaseline` over time | Low | Medium | Sprint-10.5 U2 `AdminTsCatalogContractTests` already pins `web/src/api/admin.ts` against `PermissionKeys.All`. Sprint-12 could add a parallel test pinning `dispatcherBaseline.ts` against backend `DispatcherBaseline`, OR rely on per-component tests catching missing keys. Planner decision. |

## Open Questions (resolve before planning)

None blocking. All scope decisions made in brainstorm dialogue.

## Outstanding Questions (resolve during planning)

- **U-decision** — Does the ConfirmShip button live inside the existing `order-detail-pick-actions` section (renamed to `order-detail-actions`) or a parallel `order-detail-ship-actions` section? Affects per-component test selectors. Planner picks the cleaner option after reading the existing JSX.
- **U-decision** — Does the Sprint-12 E2E test extend Sprint-11's `PickerFixture` in place or create a new `HandoffFixture`? If `PickerFixture` is parameterized over (Picker JWT only) vs (Picker + Owner + Dispatcher), the extension is clean; if not, a new fixture is less risky. Planner reads the fixture and decides.
- **U-decision** — Does Sprint-12 add a contract test pinning `dispatcherBaseline.ts` vs backend `DispatcherBaseline` (mirroring Sprint-10.5 U2's `AdminTsCatalogContractTests`)? Low-cost if added; could also be deferred to a future hygiene sprint. Planner picks.
- **U-decision** — Version bump: Sprint-12 is a net-new feature surface (third role + hand-off proof), so a minor bump `v0.16.0-sprint-12` matches Sprint-9.5 + Sprint-11 precedent. Confirm at plan time.

## Success Criteria

Sprint-12 ships when ALL of the following hold:

1. `dotnet build ShopFlow.sln` → 0 errors + 0 warnings.
2. `tests/ShopFlow.Migrate.UnitTests/` → all Sprint-11 baseline tests pass + new `DispatcherBaseline` facts pass (~55 total).
3. `tests/ShopFlow.Migrate.IntegrationTests/Provisioning/RolePermissionsSeedIntegrationTests.cs` → all 4 Sprint-11 scenarios pass + 1 new Sprint-12 fresh-tenant scenario passes (31 rows).
4. `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` exists, Skip-marked locally, runs in CI Docker tier, drives the 3-role hand-off end-to-end with 200 responses at each step.
5. `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` exists, Skip-marked locally, exercises the 4 denial paths.
6. Vitest 474-passing Sprint-11 baseline preserved + new ConfirmShip per-component coverage passes.
7. Sign-off doc lands at `docs/phase-gates/2026-05-22-sprint-12-signoff.md` mirroring Sprint-11 shape.
8. Annotated tag `v0.16.0-sprint-12` (or whatever version planner confirms) lands on `feat/sprint-12-second-non-owner-role`.
9. Branch + tag pushed to origin per standing user preference.

---

**Origin**: this document is the durable output of the 2026-05-22 brainstorm dialogue following Sprint-11 sign-off (`v0.15.0-sprint-11`). The dialogue surfaced the framing flip from "ship Dispatcher because operations needs it" to "prove the stack handles 3+ roles cleanly," then narrowed to ConfirmShip-only scope (no MarkShipFailed), Pack stays Owner-only (no Packer fourth role), and KTD1 additive-only contract inherited from Sprint-11. Planning starts from this doc, not from the Sprint-11 sign-off's "Next step" menu directly.
