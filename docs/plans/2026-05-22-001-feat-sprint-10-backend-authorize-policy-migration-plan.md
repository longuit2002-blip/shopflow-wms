---
title: "Sprint-10 — Backend [Authorize(Policy=...)] Migration"
date: 2026-05-22
status: active
depth: standard
type: feat
origin: docs/brainstorms/2026-05-22-sprint-10-backend-authorize-policy-migration-requirements.md
---

# Sprint-10 — Backend `[Authorize(Policy=...)]` Migration

## Summary

Migrate the four already-catalogued business-module controller groups from class-level `[Authorize]` / `[Authorize(Roles="Owner")]` to per-action `[Authorize(Policy = PermissionKeys.X)]` referencing the 24 keys in `PermissionKeys.All`. Per-module reflection-based unit tests pin attribute presence on every covered action. One scope addition surfaced by research: Inbound.Api gains the missing `UseAuthentication` + `UseAuthorization` middleware (verified missing — R4's premise in the brainstorm was wrong). No new keys, no frontend changes, no integration tests beyond the reflection-based unit tests — those park for Sprint-10.5.

---

## Problem Frame

Carried from the [origin brainstorm](../brainstorms/2026-05-22-sprint-10-backend-authorize-policy-migration-requirements.md). In short: Sprint-9 catalogued 24 permission keys + wired `AddShopFlowPermissionPolicies` + emitted `perm[]` JSON-array claims on every JWT. Sprint-9.5 aligned the frontend (`Sidebar` perm-filter, `requirePermission()` route guard, `RolePermissionsEditor`). The backend policy infrastructure has been idle — controllers still gate with class-level `[Authorize]` (any authenticated JWT) or `[Authorize(Roles="Owner")]` (role-name check). Today the gap is invisible because the only provisioned role is Owner and `RolePermissionsSeed` (Sprint-9 U12) bootstraps Owner with all keys. Sprint-11+'s first multi-role surface turns the gap into a privilege-escalation vector.

**Phase-1 research findings (load-bearing for the plan):**

1. **`Inbound.Api/Program.cs` has neither `UseAuthentication()` nor `UseAuthorization()` middleware.** Verified at [src/Services/Inbound/ShopFlow.Inbound.Api/Program.cs](../../src/Services/Inbound/ShopFlow.Inbound.Api/Program.cs) — only `UseProblemDetails → UseTenantRouting → MapControllers` runs. `PurchaseOrdersController` has no `[Authorize]` attribute either. Result: Inbound POs are *currently unauthenticated in production*. Sprint-10 closes that gap as a side-effect of R4.
2. **Zero existing `[Authorize(Policy=...)]` usage in production code.** Sprint-10 is the first production usage of the policy mechanism. All prior references live in docs + one Skip-marked Sprint-9.5 U9 test stub.
3. **No precedent for cross-assembly reflection tests** in the repo. The four `*.UnitTests/` projects do not currently reference any `*.Api/` project. Per-module test classes (one Authorization test class per UnitTests project) follow the established "test layout mirrors source" rule (AGENTS.md §81) and CI per-csproj matrix surfaces drift earlier than a single cross-cutting test.
4. **Frontend `web/src/api/admin.ts` permission catalog drifted from `PermissionKeys.All`** — about 9 of 12 entries don't match any backend key. Sprint-9.5 U7 `RolePermissionsEditor` ships against stale strings. Out of Sprint-10 scope (backend-only); loud-flagged for Sprint-10.5.

---

## Requirements Traceability

Origin R-IDs traced into this plan:

| Origin | Plan touch points |
|---|---|
| R1 (per-action `[Authorize(Policy=...)]` on 4 covered groups) | U1 / U2 / U3 / U4 |
| R2 (drop class-level `[Authorize]` on Inv / Out / Inb) | U1 / U2 / U3 |
| R3 (drop `[Authorize(Roles="Owner")]` on AuthAdmin) | U4 |
| R4 (PurchaseOrders attribute parity) | U3 (with scope addition for missing middleware — see KTD3) |
| R5 (no new keys; `outbound.orders.cancel` stays unapplied) | All units; KTD6 |
| R6 (reflection test per covered group) | U1 / U2 / U3 / U4 — per-module, not single cross-cutting (KTD1) |
| R7 (`[AllowAnonymous]` as opt-out) | Reflection test shape, all units |
| R8 (failure conditions — missing attribute / unknown key / mixed with AllowAnonymous) | Reflection test shape, all units |
| R9 (build clean across 47 projects) | All units' verification gate |
| R10 (existing test corpus unchanged) | All units' verification gate |
| AE1 (`InventoryController.GetSummary` → `InventoryRead`, class-level dropped) | U1 |
| AE2 (`AuthAdminController.ListUsers` → `AuthAdminUsersList`, role-gate dropped) | U4 |
| AE3 (reflection test fails on missing attribute) | All reflection tests (U1-U4) |
| AE4 (`outbound.orders.cancel` stays catalogued-but-unapplied) | KTD6 |

---

## Implementation Units

### U1. Inventory controllers — per-action `[Authorize(Policy=...)]` migration + reflection test

**Goal:** Migrate 8 actions across 3 Inventory controllers (`InventoryController` ×1, `SkusController` ×6, `AdjustmentsController` ×1) to per-action policies. Drop class-level `[Authorize]` from all three. Land the first per-module reflection-based attribute-coverage test.

**Requirements:** R1, R2, R5, R6, R7, R8, R9, R10. Covers AE1.

**Dependencies:** U0 (branch + opening commit).

**Files:**

- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/InventoryController.cs`
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/SkusController.cs`
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/AdjustmentsController.cs`
- `tests/ShopFlow.Inventory.UnitTests/Authorization/InventoryAuthorizePolicyCoverageTests.cs` (new)
- `tests/ShopFlow.Inventory.UnitTests/ShopFlow.Inventory.UnitTests.csproj` (add `<ProjectReference>` to `src/Services/Inventory/ShopFlow.Inventory.Api/ShopFlow.Inventory.Api.csproj`)

**Approach:**

- Insert `[Authorize(Policy = PermissionKeys.X)]` directly above each `[HttpVerb]` attribute on the action method. Add `using ShopFlow.SharedKernel.Authorization;` if not present.
- Remove the class-level `[Authorize]` attribute from each of the three controllers. The class-level `using Microsoft.AspNetCore.Authorization;` stays because per-action attributes need it.
- Per the canonical Inventory action-to-key mapping in [Key Technical Decisions §KTD8](#key-technical-decisions):
  - `InventoryController.Summary` → `InventoryRead`
  - `SkusController.List` / `.Ledger` → `InventoryRead`
  - `SkusController.Create` / `.Update` → `InventorySkusWrite`
  - `SkusController.SetThreshold` → `InventorySkusThresholdWrite`
  - `SkusController.SetFlashSale` → `InventorySkusFlashSaleWrite`
  - `AdjustmentsController.Adjust` → `InventoryAdjust`
- The reflection test enumerates `typeof(InventoryController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)`, filters to methods carrying an `[HttpVerb]` attribute (only those are public action surfaces), and asserts each carries exactly one `[Authorize(Policy=...)]` whose policy name is in `PermissionKeys.All`. Repeats for `SkusController` and `AdjustmentsController`.
- Reflection test references `PermissionKeys.X` constants directly in the assertion (KTD2) — a rename in the catalog surfaces as a compile error.
- CSharpier output preserved (Husky pre-commit gate).

**Patterns to follow:**

- Existing reflection pattern in [src/Shared/ShopFlow.SharedKernel/Authorization/PermissionKeys.cs](../../src/Shared/ShopFlow.SharedKernel/Authorization/PermissionKeys.cs) — `typeof(T).GetFields(BindingFlags...)` with LINQ projection.
- xUnit naming `MethodOrProperty_Scenario_ExpectedOutcome` (AGENTS.md §52).
- FluentAssertions `.Should()` style (existing in [tests/ShopFlow.SharedKernel.UnitTests/Authorization/PermissionKeysTests.cs](../../tests/ShopFlow.SharedKernel.UnitTests/Authorization/PermissionKeysTests.cs)).
- Sealed test class, no fixture / collection (matches `PermissionPolicyCompositionTests` shape).

**Test scenarios:**

- Happy path: each of the 8 Inventory actions has `[Authorize(Policy = PermissionKeys.X)]` where X is in `PermissionKeys.All`. Per-action assertions cite the expected key constant so a mismatched key fails the specific action's test, not all eight.
- Structural: each of the 3 Inventory controllers has NO class-level `[Authorize]` attribute (verified by `typeof(T).GetCustomAttribute<AuthorizeAttribute>(inherit: false)` returning null). Covers AE1's "class-level `[Authorize]` attribute has been removed" leg.
- Catalog integrity: every policy name asserted by this test class is in `PermissionKeys.All` (cross-check; a typo in the test itself fails the test).
- Negative: a synthetic test method (commented as illustrative, not deleted) demonstrates that introducing an action without `[Authorize(Policy=...)]` fails the enumerative test. Covers AE3.
- Catalog ↔ action symmetry: each Inventory key in the canonical mapping (KTD8) has at least one Inventory action attaching to it. (Inventory has no unapplied keys, unlike Outbound's `outbound.orders.cancel`.)

**Verification:**

- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings (R9).
- `dotnet test --filter "FullyQualifiedName~ShopFlow.Inventory.UnitTests"` passes including the new test class. Existing Inventory.UnitTests baseline unchanged (R10).
- CSharpier pre-commit hook passes locally.

---

### U2. Outbound `OrdersController` — per-action `[Authorize(Policy=...)]` migration + reflection test

**Goal:** Migrate 10 actions on `OrdersController` to per-action policies. Drop class-level `[Authorize]`. Land the Outbound reflection-based attribute-coverage test.

**Requirements:** R1, R2, R5, R6, R7, R8, R9, R10.

**Dependencies:** U0.

**Files:**

- `src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs`
- `tests/ShopFlow.Outbound.UnitTests/Authorization/OutboundAuthorizePolicyCoverageTests.cs` (new)
- `tests/ShopFlow.Outbound.UnitTests/ShopFlow.Outbound.UnitTests.csproj` (add `<ProjectReference>` to `src/Services/Outbound/ShopFlow.Outbound.Api/ShopFlow.Outbound.Api.csproj`)

**Approach:**

- Insert `[Authorize(Policy = PermissionKeys.X)]` above each `[HttpVerb]` attribute per the canonical Outbound mapping:
  - `Create` → `OutboundOrdersWrite`
  - `GetById` / `List` / `GetKpis` / `GetTransitions` → `OutboundOrdersRead`
  - `Seed` → `OutboundOrdersWrite` (DEV-only `IHostEnvironment.IsDevelopment()` 404 guard already present per Sprint-7 KTD; the per-action perm gate fires before that guard for non-DEV environments anyway)
  - `ConfirmPick` / `MarkPickFailed` → `OutboundOrdersPickConfirm`
  - `ConfirmPack` → `OutboundOrdersPackConfirm`
  - `ConfirmShip` → `OutboundOrdersShipConfirm`
- Remove the class-level `[Authorize]` attribute.
- `outbound.orders.cancel` key remains in `PermissionKeys.All` with no controller action attaching — no code change to `PermissionKeys.cs`, no `CancelOrder` action introduced (R5; AE4; KTD6).
- The dual-constructor pattern on `OrdersController` (Sprint-7 KTD8 — 9-arg primary + 7-arg backward-compat for legacy tests) is untouched.
- Reflection test enumerates `typeof(OrdersController)` actions same shape as U1.

**Patterns to follow:** Same as U1.

**Test scenarios:**

- Happy path: each of the 10 OrdersController actions has the expected `[Authorize(Policy=X)]`. Per-action assertions.
- Structural: `OrdersController` has NO class-level `[Authorize]` attribute.
- Catalog integrity: every policy name asserted is in `PermissionKeys.All`.
- Orphan-key note: a single test method documents that `OutboundOrdersCancel` is in the catalog but no Outbound action attaches to it. The test asserts the key still exists in `PermissionKeys.All` (would fail if a sloppy edit removed it). Covers AE4. Does NOT assert "key has at least one application" — that would be the wrong direction (KTD6).
- Negative: synthetic illustrative test demonstrating the failure shape.

**Verification:**

- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings.
- `dotnet test --filter "FullyQualifiedName~ShopFlow.Outbound.UnitTests"` passes including new test class. Sprint-7 OrdersController integration tests (Sprint-7 U4 14 tests; Sprint-7 U10 8 tests) still pass — they boot via `WebApplicationFactory<Program>` and submit JWTs with all keys via Sprint-7's baked-token flow, which already carries all Outbound keys via `RolePermissionsSeed`.

---

### U3. Inbound — auth middleware wiring + `PurchaseOrdersController` per-action policies + reflection test

**Goal:** Three-part change in one commit. (a) Add `UseAuthentication()` + `UseAuthorization()` to Inbound.Api/Program.cs between `UseProblemDetails()` and `UseTenantRouting()`. (b) Add per-action `[Authorize(Policy=...)]` to 6 actions on `PurchaseOrdersController`. (c) Land the Inbound reflection-based attribute-coverage test.

**Requirements:** R1, R4, R5, R6, R7, R8, R9, R10.

**Dependencies:** U0. **No dependency on U1 or U2** — module-isolated; can ship in any order relative to U1/U2.

**Files:**

- `src/Services/Inbound/ShopFlow.Inbound.Api/Program.cs`
- `src/Services/Inbound/ShopFlow.Inbound.Api/Controllers/PurchaseOrdersController.cs`
- `tests/ShopFlow.Inbound.UnitTests/Authorization/InboundAuthorizePolicyCoverageTests.cs` (new)
- `tests/ShopFlow.Inbound.UnitTests/ShopFlow.Inbound.UnitTests.csproj` (add `<ProjectReference>` to `src/Services/Inbound/ShopFlow.Inbound.Api/ShopFlow.Inbound.Api.csproj`)

**Approach:**

- **(a) Inbound.Api/Program.cs middleware** — insert two lines between the existing `app.UseProblemDetails();` (line 32) and `app.UseTenantRouting();` (line 33):
  - `app.UseAuthentication();`
  - `app.UseAuthorization();`
  - Matches Inventory.Api / Outbound.Api Program.cs ordering. Do NOT migrate Inbound.Api to `UseShopFlowSecurityPipeline` in this sprint — preserve the existing pattern across the three business modules (KTD4). The helper migration is a separate concern.
- Update the docstring comment block (lines 5-15) to note the Sprint-10 middleware addition and reference KTD3.
- **(b) PurchaseOrdersController attributes** — add `using Microsoft.AspNetCore.Authorization;` and `using ShopFlow.SharedKernel.Authorization;` imports. Add per-action `[Authorize(Policy=...)]` per the canonical Inbound mapping:
  - `Create` → `InboundPosWrite`
  - `GetById` / `ListOpen` → `InboundPosRead`
  - `Open` / `Cancel` → `InboundPosWrite`
  - `ReceiveLine` → `InboundReceiveConfirm`
- No class-level `[Authorize]` removal (none exists today).
- **(c) Reflection test** — same shape as U1's test class.

**Patterns to follow:**

- Inventory.Api / Outbound.Api Program.cs middleware ordering for (a).
- U1's reflection test class shape for (c).

**Test scenarios:**

- Happy path: each of the 6 PurchaseOrdersController actions has the expected `[Authorize(Policy=X)]`.
- Structural: `PurchaseOrdersController` has NO class-level `[Authorize]` (true before and after Sprint-10; the reflection test enforces it stays that way).
- Catalog integrity: every policy name asserted is in `PermissionKeys.All`.
- Negative: synthetic illustrative test demonstrating the failure shape.
- **Pre-flight verification at implementation time:** before U3 lands, scan `tests/ShopFlow.Inbound.IntegrationTests/` for any test that submits an anonymous request to `PurchaseOrdersController` and asserts 200. If one exists, that test needs a JWT seed to continue passing post-U3. If none exists, U3 ships unblocked. This is execution-time discovery, not a planning blocker (see [Risk Analysis](#risk-analysis)).

**Verification:**

- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings.
- `dotnet test --filter "FullyQualifiedName~ShopFlow.Inbound.UnitTests"` passes including new test class.
- Sprint-2-redux Inbound integration test corpus (per-module via `tests/ShopFlow.Inbound.IntegrationTests/`) does not regress — see pre-flight verification above.

---

### U4. Auth `AuthAdminController` — per-action `[Authorize(Policy=...)]` migration + drop `Roles="Owner"` + reflection test

**Goal:** Migrate 9 actions on `AuthAdminController` to per-action policies. Drop class-level `[Authorize(Roles="Owner")]`. Update the XML doc-comment to reflect the new gating model. Land the Auth reflection-based attribute-coverage test.

**Requirements:** R1, R3, R5, R6, R7, R8, R9, R10. Covers AE2.

**Dependencies:** U0. **No dependency on U1/U2/U3** — module-isolated.

**Files:**

- `src/Services/Auth/ShopFlow.Auth.Api/Controllers/AuthAdminController.cs`
- `tests/ShopFlow.Auth.UnitTests/Authorization/AuthAdminAuthorizePolicyCoverageTests.cs` (new)
- `tests/ShopFlow.Auth.UnitTests/ShopFlow.Auth.UnitTests.csproj` (add `<ProjectReference>` to `src/Services/Auth/ShopFlow.Auth.Api/ShopFlow.Auth.Api.csproj`)

**Approach:**

- Add `[Authorize(Policy = PermissionKeys.X)]` above each `[HttpVerb]` per the 1:1 AuthAdmin mapping:
  - `CreateUser` → `AuthAdminUsersCreate`
  - `ListUsers` → `AuthAdminUsersList`
  - `SetRole` → `AuthAdminUsersUpdateRole`
  - `ResetPassword` → `AuthAdminUsersResetPassword`
  - `Deactivate` → `AuthAdminUsersDeactivate`
  - `AdminMfaReset` → `AuthAdminMfaReset`
  - `AdminUnlock` → `AuthAdminLockoutUnlock`
  - `GetRolePermissions` → `AuthAdminRolePermissionsRead`
  - `UpdateRolePermissions` → `AuthAdminRolePermissionsUpdate`
- Remove the class-level `[Authorize(Roles = "Owner")]` attribute.
- Update the class-level XML doc-comment (lines 21-22 reference the role-check mechanism) to describe the per-action policy gating and the KTD13 `OwnerCritical` server-side guard as the safety net (see [Key Technical Decisions §KTD5](#key-technical-decisions)).
- Reflection test same shape as U1.

**Patterns to follow:** Same as U1.

**Test scenarios:**

- Happy path: each of the 9 AuthAdminController actions has the expected `[Authorize(Policy=X)]`. Covers AE2's "carries `[Authorize(Policy = PermissionKeys.AuthAdminUsersList)]`" leg.
- Structural: `AuthAdminController` has NO class-level `[Authorize]` or `[Authorize(Roles=...)]` attribute. Covers AE2's "class-level `[Authorize(Roles=\"Owner\")]` has been removed" leg.
- Catalog integrity: every policy name asserted is in `PermissionKeys.All` AND in `PermissionKeys.OwnerCritical` (the AuthAdmin 9 keys are the OwnerCritical 9; this dual assertion pins the KTD13 invariant from a test angle).
- Negative: synthetic illustrative test demonstrating the failure shape.

**Verification:**

- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings.
- `dotnet test --filter "FullyQualifiedName~ShopFlow.Auth.UnitTests"` passes including new test class.
- Sprint-9.5 Auth.UnitTests baseline (173 passing) unchanged.

---

### U5. Sign-off + Auth AGENTS.md update + tag

**Goal:** Capture the Sprint-10 sign-off, update Auth per-module AGENTS.md, update README + CLAUDE.md + CHANGELOG, tag `v0.14.0-sprint-10`. Standard Sprint sign-off cadence matching Sprint-7 / 8 / 8.5 / 9 / 9.5.

**Requirements:** None directly — documentation + release marker.

**Dependencies:** U1, U2, U3, U4.

**Files:**

- `docs/phase-gates/2026-05-22-sprint-10-signoff.md` (new)
- `src/Services/Auth/AGENTS.md` (light update; ≤ 50 lines per root AGENTS.md §82)
- `README.md` (current-stage block)
- `CLAUDE.md` (current-stage block + Sprint-10 added to history)
- `CHANGELOG.md` (Sprint-10 entry; matches Sprint-9.5 entry shape)

**Approach:**

- Sign-off doc mirrors [Sprint-9.5 sign-off](../phase-gates/2026-05-21-sprint-9.5-signoff.md) shape (~7 sections — units / KTDs / trade-offs / verification gates / next step). Names every catalogued-but-unapplied key (`outbound.orders.cancel` + `hub.connect`) per [Success Criteria](#success-criteria) bullet 5.
- Auth `AGENTS.md` adds a line noting "after Sprint-10, controllers carry per-action `[Authorize(Policy = PermissionKeys.X)]`; class-level `[Authorize(Roles=\"Owner\")]` removed; KTD13 OwnerCritical server-side guard in `RolePermissionsCommandHandler` + `RolePermissionsSeed` bootstrapping are the safety nets." Stays within the 50-line file budget.
- Tag `v0.14.0-sprint-10` annotated, pointing at U5 HEAD.
- After tagging, push branch + tag to origin per the standing user preference (memory feedback `push-before-phase-switch`).

**Patterns to follow:**

- Sprint-9.5 U10 sign-off commit (`bcb4b96`) — same shape, same section ordering, same KTD count.
- Sprint-9 U17 sign-off commit (`c0c3ad9`) — same Auth AGENTS.md update cadence.

**Test scenarios:** None — pure documentation + git operations.

`Test expectation: none -- documentation and tag commit; no behavioral change.`

**Verification:**

- Sign-off doc exists at the named path and references each of U0-U5 commits.
- Auth `AGENTS.md` ≤ 50 lines.
- `CLAUDE.md` current-stage block reflects Sprint-10 completion.
- Tag `v0.14.0-sprint-10` exists at HEAD: `git rev-parse v0.14.0-sprint-10` resolves; `git show v0.14.0-sprint-10` shows the U5 sign-off commit.
- Push to origin succeeds (branch + tag).

---

### U0. Branch cut + opening commit with plan + KTDs

**Goal:** Cut `feat/sprint-10-backend-authorize-policy-migration` from `v0.13.0-sprint-9.5`. Opening commit carries the brainstorm + this plan + 8 KTDs in the commit body. Standard Sprint U0 cadence matching Sprint-7 U0 / Sprint-8 U0 / Sprint-9 U0 / Sprint-9.5 U0.

**Requirements:** None — process unit.

**Dependencies:** None — first unit.

**Files:**

- `docs/brainstorms/2026-05-22-sprint-10-backend-authorize-policy-migration-requirements.md` (already on disk; staged)
- `docs/plans/2026-05-22-001-feat-sprint-10-backend-authorize-policy-migration-plan.md` (this file; staged)

**Approach:**

- Push current `feat/sprint-9.5-notification-frontend-integration-tests` branch + `v0.13.0-sprint-9.5` tag to origin per memory feedback.
- `git checkout v0.13.0-sprint-9.5` then `git checkout -b feat/sprint-10-backend-authorize-policy-migration`.
- Stage the brainstorm doc + this plan doc.
- Commit with subject matching the established sprint pattern: `feat(sprint-10 U0): branch cut + brainstorm + plan + 8 KTDs`.
- Body of commit lists the 8 KTDs verbatim (KTD1-KTD8 per [Key Technical Decisions](#key-technical-decisions)).

**Patterns to follow:**

- Sprint-9.5 U0 (`d0022cd`).
- Sprint-9 U0 (`e82101c`).

**Test scenarios:** None — branch cut.

`Test expectation: none -- branch cut + docs only; no code change.`

**Verification:**

- Branch `feat/sprint-10-backend-authorize-policy-migration` exists locally + on origin.
- `git log --oneline -1` shows the U0 commit with KTDs in body.
- `git status` clean except for any node_modules / settings.json drift.

---

## Scope Boundaries

Carried verbatim from origin plus plan-time additions. Single list since this is a Standard tier (not Deep-product).

### In-sprint scope additions surfaced during planning

- **Inbound.Api `UseAuthentication()` + `UseAuthorization()` middleware** — small ~3-line addition to [src/Services/Inbound/ShopFlow.Inbound.Api/Program.cs](../../src/Services/Inbound/ShopFlow.Inbound.Api/Program.cs). Required precondition for R4 (PurchaseOrdersController policies would silently no-op without it). Closes the side-effect security gap that Inbound POs are currently unauthenticated. Lands in U3.

### Carried from origin (unchanged)

- No new permission keys; `PermissionKeys.All` stays at 24.
- Ungated live controllers (`ChannelController`, `ProductMappingsController`, `SkuFlagsController`, `SyncStateController`, `PutAwayController`) stay ungated.
- Stub controllers (`OutboundController`, `AnalyticsController`) stay ungated.
- `WebhooksController` (Channel) keeps `[AllowAnonymous]` (HMAC-gated).
- `AuthController` public surface (login / refresh / forgot-password / reset-password-confirm / mfa-verify) keeps `[AllowAnonymous]`. Self-service endpoints (logout / me-password / mfa-enroll-begin / mfa-disable / mfa-recovery-codes) keep bare `[Authorize]` — no perm key exists for self-service over the authenticated user, by design (KTD7).
- No frontend changes.
- No `[Authorize(Policy = PermissionKeys.HubConnect)]` on `TenantHub`.
- No 403 wire-shape integration tests.
- No role-mapping pin tests.
- No default-deny global filter; no Roslyn analyzer `ShopFlow0005`.

### Deferred to Follow-Up Work

- **Sprint-10.5 — frontend per-button gating + `hub.connect` application + 403 wire-shape integration tests + role-mapping pin tests.** Same point-release cadence as Sprint-2.5 / 4.5 / 7.5 / 8.5 / 9.5.
- **Sprint-10.5 — fix frontend `web/src/api/admin.ts` `PERMISSION_KEYS` catalog drift.** Phase-1 research surfaced ~9 of 12 frontend strings out of sync with backend `PermissionKeys.All` (`outbound.orders.confirm-pick` vs backend `outbound.orders.pick-confirm`; `inventory.skus.create`/`.update` vs single `inventory.skus.write`; `inbound.pos.create` vs `inbound.pos.write`; `inbound.receiving.confirm` vs `inbound.receive.confirm`; `hub.tenant.read`/`hub.tenant.write` vs `hub.connect`; `notification.dlq.read` doesn't exist; `outbound.orders.mark-pick-failed` doesn't exist). Sprint-9.5 U7 `RolePermissionsEditor` ships against stale strings — invisible today because only Owner is provisioned. Sprint-10.5 must include a unit to align the frontend catalog AND add a contract test (frontend or integration) pinning the alignment.
- **Future sprint — add `CancelOrder` action to `OrdersController`** to attach `outbound.orders.cancel` (the orphan key in the Outbound catalog). Or remove the key when the canonical decision is made that Outbound never gets a Cancel surface (compensation flows via saga, not direct API).
- **Future sprint — migrate Inventory.Api + Outbound.Api Program.cs to `UseShopFlowSecurityPipeline`** for consistency with Auth.Api's pattern (Sprint-9 KTD7). Existing hand-wiring works correctly for `[Authorize(Policy=...)]`; the migration is a cleanup, not a functional change.

---

## Key Technical Decisions

- **KTD1 — Per-module reflection tests in `tests/ShopFlow.<Module>.UnitTests/Authorization/` rather than a single cross-cutting test in `ShopFlow.SharedKernel.UnitTests/`.** Rationale: (a) CI per-csproj matrix surfaces drift earlier (per Sprint-8.5 R13 convention); (b) each module's UnitTests project gains one `<ProjectReference>` to its own Api project rather than SharedKernel.UnitTests gaining 4 cross-module Api references (less coupling); (c) AGENTS.md §81 says "test layout mirrors source"; (d) the per-module learning [docs/solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md](../solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md) recommended this shape. Resolves origin Outstanding Question 1.

- **KTD2 — Reflection tests reference `PermissionKeys.X` constants directly (not string literals).** Rationale: a rename of any key in the catalog surfaces as a compile error in the test. String literal would silently pass against a renamed key, masking drift. Per the same `contracts-evolution-consumer-test-sweep` learning.

- **KTD3 — Inbound.Api Program.cs gains `UseAuthentication()` + `UseAuthorization()` in U3.** Rationale: the brainstorm's R4 assumption (PurchaseOrdersController "relies on global JwtBearer middleware for authentication") is verified false — Inbound.Api has neither middleware. Per-action `[Authorize(Policy=...)]` attributes would silently no-op without this. Side-effect: Inbound POs become authenticated-by-default after Sprint-10 (they were anonymous-by-default since Sprint-2-redux). Behaviorally fine because no production frontend currently calls Inbound endpoints (frontend `/inbound` route is the Sprint-6 `ComingSoon` placeholder).

- **KTD4 — Inventory.Api + Outbound.Api keep hand-wired `UseAuthentication()` + `UseAuthorization()`.** Rationale: their existing middleware ordering already works for `[Authorize(Policy=...)]`. Migration to `UseShopFlowSecurityPipeline` is a separate cleanup; bundling it into Sprint-10 expands scope unnecessarily. Inbound.Api adopts the same hand-wired pattern in U3 rather than the helper, for cross-business-module consistency.

- **KTD5 — Class-level `[Authorize]` and `[Authorize(Roles="Owner")]` are FULLY DROPPED (not belt-and-braces).** Rationale: per origin Key Decision 1 — permissions are the single canonical gate. Safety nets:
  - `RolePermissionsSeed` (Sprint-9 U12) bootstraps the Owner role with all 24 keys at every tenant provision.
  - `OwnerCritical` server-side guard (KTD13 in `RolePermissionsCommandHandler`, Sprint-9 U8) rejects any `UpdateRolePermissions` request that would shed an admin key from the Owner row.
  - U4 reflection test asserts the 9 AuthAdmin keys are exactly the 9 in `PermissionKeys.OwnerCritical` (dual-pin).

- **KTD6 — Catalogued-but-unapplied keys (`outbound.orders.cancel` + `hub.connect`) stay in `PermissionKeys.All` at Sprint-10 sign-off.** Rationale: removing them would force key-removal commits for orphans (opposite direction). Adding stub actions just to attach the keys is fake coverage. Honest state: 22 of 24 keys are applied; 2 wait for their attachment surface (`CancelOrder` action future; `hub.connect` Sprint-10.5). Reflection tests do NOT enforce "every catalogued key has at least one application."

- **KTD7 — `AuthController` self-service endpoints keep bare `[Authorize]`.** Rationale: logout / me-password / mfa-enroll-begin / mfa-disable / mfa-recovery-codes operate on the authenticated user's own account. Authentication is the necessary and sufficient gate; perm-checking adds no meaningful guarantee (the user has implicit authority over their own account). No perm keys exist for "logout self" or "change my own password" by design.

- **KTD8 — Canonical action-to-key mapping table (33 actions across 6 controllers, 4 controller groups):**

  | Controller | Action | Permission key |
  |---|---|---|
  | `InventoryController` | `Summary` | `InventoryRead` |
  | `SkusController` | `List` | `InventoryRead` |
  | `SkusController` | `Ledger` | `InventoryRead` |
  | `SkusController` | `Update` (PUT `/{sku}`) | `InventorySkusWrite` |
  | `SkusController` | `Create` | `InventorySkusWrite` |
  | `SkusController` | `SetThreshold` | `InventorySkusThresholdWrite` |
  | `SkusController` | `SetFlashSale` | `InventorySkusFlashSaleWrite` |
  | `AdjustmentsController` | `Adjust` | `InventoryAdjust` |
  | `OrdersController` | `Create` | `OutboundOrdersWrite` |
  | `OrdersController` | `GetById` | `OutboundOrdersRead` |
  | `OrdersController` | `List` | `OutboundOrdersRead` |
  | `OrdersController` | `GetKpis` | `OutboundOrdersRead` |
  | `OrdersController` | `GetTransitions` | `OutboundOrdersRead` |
  | `OrdersController` | `Seed` (DEV-only) | `OutboundOrdersWrite` |
  | `OrdersController` | `ConfirmPick` | `OutboundOrdersPickConfirm` |
  | `OrdersController` | `MarkPickFailed` | `OutboundOrdersPickConfirm` |
  | `OrdersController` | `ConfirmPack` | `OutboundOrdersPackConfirm` |
  | `OrdersController` | `ConfirmShip` | `OutboundOrdersShipConfirm` |
  | `PurchaseOrdersController` | `Create` | `InboundPosWrite` |
  | `PurchaseOrdersController` | `GetById` | `InboundPosRead` |
  | `PurchaseOrdersController` | `ListOpen` | `InboundPosRead` |
  | `PurchaseOrdersController` | `Open` | `InboundPosWrite` |
  | `PurchaseOrdersController` | `Cancel` | `InboundPosWrite` |
  | `PurchaseOrdersController` | `ReceiveLine` | `InboundReceiveConfirm` |
  | `AuthAdminController` | `CreateUser` | `AuthAdminUsersCreate` |
  | `AuthAdminController` | `ListUsers` | `AuthAdminUsersList` |
  | `AuthAdminController` | `SetRole` | `AuthAdminUsersUpdateRole` |
  | `AuthAdminController` | `ResetPassword` | `AuthAdminUsersResetPassword` |
  | `AuthAdminController` | `Deactivate` | `AuthAdminUsersDeactivate` |
  | `AuthAdminController` | `AdminMfaReset` | `AuthAdminMfaReset` |
  | `AuthAdminController` | `AdminUnlock` | `AuthAdminLockoutUnlock` |
  | `AuthAdminController` | `GetRolePermissions` | `AuthAdminRolePermissionsRead` |
  | `AuthAdminController` | `UpdateRolePermissions` | `AuthAdminRolePermissionsUpdate` |

  Action-name confirmation: `OrdersController.GetTransitions` → `OutboundOrdersRead` (per research: consumed by `<TransitionsLog>` on the orders detail page, parallel to `fetchOrderDetail`, semantically a read). `OrdersController.MarkPickFailed` → `OutboundOrdersPickConfirm` (operator's failure-reporting action within the pick-flow lifecycle; cancellation is a downstream saga consequence, not the operator's intent).

---

## Success Criteria

Carried from origin:

- Every public action on the four covered controller groups has an explicit `[Authorize(Policy=...)]` attribute. The class-level catch-all is gone.
- The per-module reflection-based unit tests pass after the migration and fail fast with a clear, actionable message if a future PR adds an ungated action to a covered controller.
- A request to any covered-controller endpoint with a valid JWT that lacks the required permission key returns 403 — the frontend Sprint-9.5 perm gate is now backed by a backend gate consulting the same `perm[]` claim. (Verified at runtime via Sprint-10.5 integration tests; planning-time confidence rests on the policy registration shape from `AddShopFlowPermissionPolicies` which is contract-pinned by Sprint-9.5 `PermissionPolicyCompositionTests`.)
- Sprint-10.5 inherits a clean attribute landscape on the four covered controller groups; its frontend-button-gating + `hub.connect` work focuses on the remaining surface without doubling back to the backend.
- The Sprint-10 sign-off names the catalogued-but-unapplied keys (`outbound.orders.cancel`, `hub.connect`) explicitly so the next sprint can decide whether to attach them, remove them, or extend them.

Plan-specific addition:

- Inbound.Api becomes authenticated-by-default after U3 (previously anonymous-by-default since Sprint-2-redux). The Inbound module's API surface joins Inventory + Outbound + Auth in requiring a valid JWT for every endpoint.

---

## System-Wide Impact

Sprint-10 changes the *gating mechanism* on 33 actions but preserves their *behavior* for the only currently-provisioned role (Owner). Operationally:

- **No data model changes.** No new tables, columns, migrations, or contracts.
- **No new permission keys.** `PermissionKeys.All` unchanged.
- **No frontend changes.** Sidebar perm-filter, `requirePermission()` route guard, `RolePermissionsEditor` all continue to work as Sprint-9.5 shipped them (with the caveat that admin.ts permission catalog drift makes the editor's UX correctness brittle once non-Owner roles exist — flagged for Sprint-10.5).
- **Inbound.Api authentication posture changes.** Previously, requests to Inbound endpoints bypassed authentication entirely (middleware missing); after U3, requests require a valid JWT. No production callers expected (frontend `/inbound` is `ComingSoon`); the Sprint-2-redux Inbound integration tests need a pre-flight check during U3 implementation to confirm none of them assert anonymous 200.
- **Future tenant provisioning unchanged.** `RolePermissionsSeed` already reads `PermissionKeys.All` reflectively at every tenant provision (Sprint-9 U12) — new tenants automatically get the seeded Owner with all 24 keys; no Sprint-10 change needed to that path.
- **No deployment changes.** Same containers, same env vars, same secrets, same migration entrypoint.

---

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Owner role accidentally locked out of admin surface after dropping `Roles="Owner"` from `AuthAdminController`. | Low | High | `RolePermissionsSeed` (Sprint-9 U12) reads `PermissionKeys.All` reflectively + inserts ALL keys for Owner at every tenant provision. `OwnerCritical` server-side guard (KTD13) blocks any role-permission edit that would shed admin keys from Owner. U4 reflection test pins that the 9 AuthAdmin keys equal `PermissionKeys.OwnerCritical` (dual pin). |
| Inbound.IntegrationTests has a test that asserts anonymous-200 on `PurchaseOrdersController` — would break when U3 wires `UseAuthentication()`. | Low-Medium | Medium | U3's pre-flight verification step explicitly scans `tests/ShopFlow.Inbound.IntegrationTests/` before landing. If such a test exists, the U3 commit also patches it to seed a JWT. Sprint-2-redux Inbound integration tests historically test repository + service-level paths, not WebApplicationFactory HTTP paths — confidence is high that no such anonymous-200 assertion exists, but the implementer verifies at execution time. |
| Frontend RolePermissionsEditor breaks worse after Sprint-10 because backend now enforces keys the frontend has always sent stale versions of. | Low | Low | Today only Owner is provisioned; Owner has all keys via `RolePermissionsSeed`. The editor's "save" path goes through `RolePermissionsCommandHandler` which validates against the backend `PermissionKeys.All`. Stale keys from frontend would fail validation at save time (or be silently dropped, depending on Sprint-9 U8 impl detail — implementer can confirm). The flagged Sprint-10.5 fix closes the drift. |
| CSharpier reflows the attribute additions in unexpected ways, breaking the Husky pre-commit gate. | Low | Low (slows down work, not correctness) | Each unit's verification step explicitly includes "CSharpier pre-commit hook passes locally." If reflow happens, implementer accepts the CSharpier output and re-stages. |
| Cross-project `<ProjectReference>` additions in `*.UnitTests.csproj` files trigger new CPM warnings or transitive dependency conflicts. | Low | Low | The four Api projects don't carry domain dependencies that conflict with their own UnitTests projects. If a warning surfaces, the verification gate (R9 — 0 warnings) catches it immediately. |

---

## Dependencies / Prerequisites

Carried from origin plus plan-time additions:

- **`PermissionKeys.All` + `AddShopFlowPermissionPolicies` stable** — Sprint-9 U6 / U7 / U12 shipped; load-bearing. Verified at [src/Shared/ShopFlow.SharedKernel/Authorization/](../../src/Shared/ShopFlow.SharedKernel/Authorization/).
- **Owner role seeded with all 24 keys** — Sprint-9 U12 `RolePermissionsSeed` reads `PermissionKeys.All` reflectively. Verified.
- **JwtBearer wired in `AddShopFlowDefaults`** — Sprint-7 U5 lift, Sprint-9 U7 policy registration. Verified.
- **The covered-controller list is exhaustive** — 4 controller groups, 33 actions. Verified during research.
- **`perm` claim emitted as JSON `string[]`** — Sprint-9 KTD1, pinned by [JwtTokenIssuerTests](../../tests/ShopFlow.Auth.UnitTests/) (Sprint-9 U6 +3 tests). Sprint-10 does NOT reshape the claim; the [perm-claim-must-be-json-array](../solutions/2026-05-20-perm-claim-must-be-json-array.md) learning is the canonical reference.
- **CI per-csproj matrix runs `tests/ShopFlow.<Module>.UnitTests/`** — the new reflection test classes land in CI's per-PR gate (`build-and-unit-test` job). Verified at [.github/workflows/ci.yml](../../.github/workflows/ci.yml).
- **Husky pre-commit `dotnet csharpier --check .` enforced locally** — verified at [.husky/pre-commit](../../.husky/pre-commit).
- **`Directory.Build.props` carries `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`** — any new warning fails the build (R9).
- **Branch state at `bcb4b96` = `v0.13.0-sprint-9.5`** — clean working tree (modulo `.claude/settings.json` + `node_modules/`); safe to cut Sprint-10 branch.

---

## Verification Strategy

Per-unit verification listed in each Implementation Unit. Sprint-wide gates aggregated:

1. **Build**: `dotnet build ShopFlow.sln` → 0 errors + 0 warnings across all 47 projects (R9). Enforced unit-by-unit + at sign-off.
2. **Unit tests**: per-module `*.UnitTests/` projects all pass including the 4 new `Authorization/<Module>AuthorizePolicyCoverageTests.cs` classes. Sprint-9.5 baseline (`tests/ShopFlow.Auth.UnitTests/` 173, `tests/ShopFlow.Notification.UnitTests/` 66, `tests/ShopFlow.SharedKernel.UnitTests/` 47, etc.) unchanged (R10).
3. **CSharpier**: `dotnet csharpier --check .` passes locally (Husky pre-commit) and in CI.
4. **Reflection contract**: each per-module reflection test asserts (a) every public action on covered controllers carries `[Authorize(Policy = PermissionKeys.X)]`, (b) X is in `PermissionKeys.All`, (c) covered controllers have no class-level `[Authorize]` / `[Authorize(Roles=...)]`. Failure surfaces controller-name + method-name + missing-attribute (AE3).
5. **Behavioral**: no behavioral test in this sprint. Sprint-10.5 lands 403 wire-shape integration tests; Sprint-10 trusts the policy mechanism contract pinned by Sprint-9 `PermissionPolicyCompositionTests`.
6. **Tag + push**: `v0.14.0-sprint-10` annotated tag exists at U5 HEAD; branch + tag pushed to origin.

---

## Outstanding Questions

### Resolve Before Planning

None — all resolved during Phase 1 research + the brainstorm Phase 1.3 dialogue.

### Deferred to Implementation

- **[Affects U1-U4][Technical]** Whether reflection tests reference `PermissionKeys.X` directly (`Assert that policy.Name == PermissionKeys.InventoryRead`) or via `nameof(PermissionKeys.InventoryRead)`. Both produce compile errors on rename. The constant IS a string at runtime; the `nameof` returns the field name (`"InventoryRead"`) which doesn't match the constant value (`"inventory.read"`). KTD2 prescribes the constant directly — but if the test's narrative reads cleaner with `nameof`, that's a tactical call.
- **[Affects U3][Technical]** Whether `Inbound.Api/Program.cs` exposes `Program` as `public partial class Program` so future `WebApplicationFactory<Program>` integration tests can boot it (Sprint-6 pattern). Today Inventory.Api / Outbound.Api / Auth.Api do; Inbound.Api does not. Adding it now (small change) prepares Sprint-10.5's integration test work; deferring it is also valid. Implementer picks.
- **[Affects U3][Pre-flight verification]** Whether any existing test in `tests/ShopFlow.Inbound.IntegrationTests/` submits an anonymous request to `PurchaseOrdersController` and asserts 200. Quick grep at U3 implementation time; patch if found.
- **[Affects U1-U4][Convention]** Whether the synthetic "illustrative failure-shape test" mentioned in each unit's test scenarios should be a commented xUnit method (`// [Fact(Skip = "Illustrative — uncomment locally to verify the negative path")]`) or a separate test type that runs against a fixture-internal stub controller. The commented approach is cheaper; the fixture-stub approach is more rigorous. Implementer picks based on the readability of the resulting test file.
- **[Affects U4][Documentation]** The exact wording for `AuthAdminController`'s class-level XML doc-comment rewrite — should it cross-reference KTD13 and `RolePermissionsSeed` directly, or stay abstract about the safety nets? Implementer's call; the contract surfaces a working class doc-comment either way.

Each of these is execution-time discovery — answerable by reading code or running grep at the moment of implementation, not by additional planning research.
