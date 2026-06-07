---
date: 2026-05-22
topic: sprint-10-backend-authorize-policy-migration
---

# Sprint-10 — Backend `[Authorize(Policy=...)]` Migration

## Summary

Migrate the four already-catalogued business-module controller groups (Inventory × 3, Outbound `OrdersController`, Inbound `PurchaseOrdersController`, Auth `AuthAdminController`) from class-level `[Authorize]` / `[Authorize(Roles="Owner")]` to per-action `[Authorize(Policy=...)]` referencing the 24 keys already in `PermissionKeys.All`. Per-action policies become the single canonical gate; no new keys are added. Reflection-based unit tests pin attribute presence across every covered action.

---

## Problem Frame

Sprint-9 backend hardening catalogued 24 named permissions in [`src/Shared/ShopFlow.SharedKernel/Authorization/PermissionKeys.cs`](../../src/Shared/ShopFlow.SharedKernel/Authorization/PermissionKeys.cs), registered one ASP.NET Core authorization policy per key via [`AddShopFlowPermissionPolicies`](../../src/Shared/ShopFlow.SharedKernel/Authorization/PermissionPolicyExtensions.cs), and emitted those keys to every access token as a JSON `perm[]` claim (Sprint-9 U6, KTD1). Sprint-9.5 then aligned the frontend — Sidebar `permRequired` filtering, `requirePermission()` route guards, `RolePermissionsEditor` Owner-locked editor — so the JWT `perm[]` claim drives client-side gating.

The backend itself has not moved. Every business-module controller still gates with class-level `[Authorize]` (any authenticated JWT passes) or `[Authorize(Roles="Owner")]` (role-name check). The policy infrastructure is idle. A user whose `perm[]` claim does not include `inventory.adjust` is hidden from the "Adjust stock" button by frontend gating, but can still call `POST /api/inventory/adjustments` directly with their JWT and succeed. The frontend gate is security theatre until the backend honors the same claim.

The cost is bounded today because the only provisioned role is Owner, and Sprint-9 U12 `RolePermissionsSeed` gives Owner all 24 keys — so every issued JWT carries every key and the gap is invisible. The moment a non-Owner role exists with a narrower key-set — the first multi-role surface, the natural sprint after this one — the gap becomes a privilege-escalation vector.

Sprint-9's sign-off names this explicitly: *"AuthAdminController class-level `Roles=\"Owner\"` PRESERVED (additive — Sprint-10+ flips to per-action policies)."* Sprint-9.5 trade-off #2 carries it forward as: *"Per-route + per-action-button `usePerm` application across Inventory / Orders / Inbound → Sprint-10+ alongside backend `[Authorize(Policy=...)]` migration."* Sprint-10 closes the backend half; Sprint-10.5 closes the frontend per-button half.

---

## Requirements

**Backend attribute migration**

- R1. Every public action method on the four covered controller groups carries an `[Authorize(Policy=...)]` attribute whose policy name is a key in `PermissionKeys.All`. The four covered controller groups are:
  - [`InventoryController`](../../src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/InventoryController.cs), [`SkusController`](../../src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/SkusController.cs), [`AdjustmentsController`](../../src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/AdjustmentsController.cs)
  - [`OrdersController`](../../src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs)
  - [`PurchaseOrdersController`](../../src/Services/Inbound/ShopFlow.Inbound.Api/Controllers/PurchaseOrdersController.cs)
  - [`AuthAdminController`](../../src/Services/Auth/ShopFlow.Auth.Api/Controllers/AuthAdminController.cs)
- R2. Class-level `[Authorize]` is removed from `InventoryController`, `SkusController`, `AdjustmentsController`, and `OrdersController`. Per-action policies inherit `RequireAuthenticatedUser()` from `AddShopFlowPermissionPolicies`, so authentication enforcement is preserved.
- R3. Class-level `[Authorize(Roles="Owner")]` is removed from `AuthAdminController`. Per-action policies replace the role check. The `OwnerCritical` server-side guard (KTD13) in `RolePermissionsCommandHandler` remains the safety net against any future role-permission edit that would shed an admin key from the Owner row.
- R4. `PurchaseOrdersController` is brought to attribute parity — every action gains `[Authorize(Policy=...)]`. Today it carries no class-level `[Authorize]` and relies on global JwtBearer middleware for authentication. Per-action policies make the gate explicit at the call site.
- R5. The action-to-key mapping uses the existing 24 keys in `PermissionKeys.All`. No new keys are added. No keys are removed. The `outbound.orders.cancel` key remains catalogued-but-unapplied (no Cancel action exists on `OrdersController` today).

**Attribute test coverage**

- R6. A new unit test class enumerates every public action method on the four covered controller groups via reflection and asserts each one carries exactly one `[Authorize(Policy=...)]` attribute whose policy name is in `PermissionKeys.All`.
- R7. The reflection test treats `[AllowAnonymous]` as an explicit opt-out — actions marked `[AllowAnonymous]` are skipped from the policy-presence check. None are expected in the covered controllers; the rule exists so the test does not need editing if a future covered-controller action legitimately opens up.
- R8. The reflection test fails — does not warn — when any of the following hold on a covered controller:
  - An action lacks `[Authorize(Policy=...)]`
  - An action carries `[Authorize(Policy=X)]` where `X` is not in `PermissionKeys.All`
  - An action carries both `[Authorize(Policy=...)]` and `[AllowAnonymous]`

**Build & verification gates**

- R9. `dotnet build ShopFlow.sln` produces 0 errors and 0 warnings across all 47 projects.
- R10. The existing unit-test corpus (Sprint-9.5 baseline) continues to pass unmodified. Sprint-10 adds tests; it does not modify or delete existing tests.

---

## Acceptance Examples

- AE1. **Covers R1, R2, R5.** Given the `InventoryController.GetSummary` action, when Sprint-10 ships, the action carries `[Authorize(Policy = PermissionKeys.InventoryRead)]` and the class-level `[Authorize]` attribute has been removed. The action's behavior is unchanged for any JWT whose `perm[]` claim contains `inventory.read`.
- AE2. **Covers R3.** Given the `AuthAdminController.ListUsers` action, when Sprint-10 ships, the action carries `[Authorize(Policy = PermissionKeys.AuthAdminUsersList)]` and the class-level `[Authorize(Roles="Owner")]` has been removed. A request with a valid JWT whose `perm[]` claim lacks `auth.admin.users.list` receives a 403 — the same response status that today's role-check failure produces.
- AE3. **Covers R6, R8.** Given a new controller action `FooController.Bar` is added in a future PR without `[Authorize(Policy=...)]` and without `[AllowAnonymous]`, and `FooController` is one of the four covered controller groups, the reflection unit test fails with a message naming the controller, the action, and the missing attribute.
- AE4. **Covers R5.** Given the `outbound.orders.cancel` key exists in `PermissionKeys.All` but no `CancelOrder` action exists on `OrdersController` today, Sprint-10 does not introduce a `CancelOrder` action and does not remove the key. The key remains catalogued-but-unapplied alongside `hub.connect`.

---

## Success Criteria

- Every public action on the four covered controller groups has an explicit `[Authorize(Policy=...)]` attribute. The class-level catch-all is gone.
- The reflection-based unit test passes after the migration and fails fast with a clear, actionable message if a future PR adds an ungated action to a covered controller.
- A request to any covered-controller endpoint with a valid JWT that lacks the required permission key returns 403 — the frontend Sprint-9.5 perm gate is now backed by a backend gate consulting the same `perm[]` claim.
- Sprint-10.5 inherits a clean attribute landscape on the four covered controller groups, so its frontend-button-gating + `hub.connect` work focuses on the remaining surface without doubling back to the backend.
- The Sprint-10 sign-off names the catalogued-but-unapplied keys (`outbound.orders.cancel`, `hub.connect`) explicitly so the next sprint can decide whether to attach them, remove them, or extend them.

---

## Scope Boundaries

- **No new permission keys.** `PermissionKeys.All` ships Sprint-10 with the same 24 keys it had at Sprint-9.5. Closing the ungated-controller gap (Channel × 2, StockSync × 2, PutAway) requires new keys and is a separate sprint.
- **Ungated live controllers stay ungated.** `ChannelController`, `ProductMappingsController`, `SkuFlagsController`, `SyncStateController`, and `PutAwayController` carry no `[Authorize]` attributes today and remain so after Sprint-10. Stubs (`OutboundController`, `AnalyticsController`) likewise unchanged. `WebhooksController` keeps its `[AllowAnonymous]` (HMAC-gated).
- **`AuthController` public surface is untouched.** Login / refresh / forgot-password / reset-password-confirm / mfa-verify keep their `[AllowAnonymous]` attributes. Self-service endpoints (logout / me-password / mfa-enroll-begin / mfa-disable / mfa-recovery-codes) keep their bare `[Authorize]` attribute — no permission keys exist for "logout self" or "change my own password" by design. Self-service over the authenticated user is the user's own scope; perm-gating it adds no meaningful guarantee.
- **No frontend changes.** Sidebar, route guards, `RolePermissionsEditor`, page-level perm checks all stay where Sprint-9.5 left them. Per-button `usePerm()` gating across business pages rides Sprint-10.5.
- **No `hub.connect` attribute on `TenantHub`.** The key exists in the catalog from Sprint-9 U7 but the hub class itself is unmodified. Sprint-10.5.
- **No 403 wire-shape integration tests.** Reflection-based unit tests assert attribute presence; runtime behavior pinning (request → 403 + problem-details `errorCode`) waits for Sprint-10.5 where the Skip-marked Sprint-9.5 U9 fixture work already needs revisiting.
- **No role-mapping pin tests.** Tests that codify "Owner has all 24 keys; Picker has read + pick-confirm only; etc." belong with the integration-test work in Sprint-10.5.
- **No default-deny global filter, no Roslyn analyzer.** Forward-compatibility safety nets (a global `IAuthorizationFilter` that rejects any action without `[Authorize(Policy=...)]` or `[AllowAnonymous]`, or a `ShopFlow0005` analyzer that fails build on missing attributes) are out of scope. The reflection unit test is the chosen guard.

---

## Key Decisions

- **Permissions are the single canonical gate.** Drop all class-level `[Authorize]` / `[Authorize(Roles="Owner")]` rather than running per-action policies alongside them. Belt-and-braces would contradict the "permissions are source of truth" stance Sprint-9 established. The KTD13 `OwnerCritical` server-side guard in `RolePermissionsCommandHandler` is the safety net against accidental admin-key erosion from the Owner row, and `RolePermissionsSeed` (Sprint-9 U12) ensures the Owner role starts with all admin keys at every tenant provision.
- **Reflection-based unit test, not Roslyn analyzer, not global filter.** A reflection test runs in the existing xUnit corpus, fails fast in CI, and has no runtime cost. A Roslyn analyzer (`ShopFlow0005`) is forward-compatible across the whole codebase but is more work and adds a fifth analyzer to the four already shipping. A global default-deny filter changes the framework default for every controller including the 5 ungated ones — too much blast radius for a "Minimal" scope sprint.
- **Backend-only sprint, Sprint-10.5 follows.** Matches the Sprint-2.5 / 4.5 / 7.5 / 8.5 / 9.5 cadence — small focused sprints with clean tag boundaries, easier to revert, easier to attribute regressions to. Bundling frontend + `hub.connect` + integration tests + role-mapping pin tests into one sprint would produce a Sprint-9-sized commit train, not the ~1-week point release this is sized for.
- **Catalogued-but-unapplied keys are acceptable transient state.** `outbound.orders.cancel` (no `CancelOrder` action exists today) and `hub.connect` (no attribute on `TenantHub` yet) end Sprint-10 in the catalog with no controller attribute. Documented in sign-off; tracked as carry-forward; addressed in Sprint-10.5 (`hub.connect`) or a future sprint that introduces a `CancelOrder` endpoint (`outbound.orders.cancel`). The reflection test does NOT enforce "every catalogued key has at least one application" — that test would force key-removal commits for orphans, which is the opposite direction of the migration.

---

## Dependencies / Assumptions

- **`PermissionKeys.All` and `AddShopFlowPermissionPolicies` are stable and load-bearing.** Sprint-9 U7 shipped the policy registration; Sprint-9 U6 shipped the JWT `perm[]` claim emission; Sprint-9 U12 shipped the `RolePermissionsSeed` Owner-all-keys bootstrap. Verified against the codebase at `src/Shared/ShopFlow.SharedKernel/Authorization/`.
- **The Owner role seed gives the Owner role every key in `PermissionKeys.All`** at every tenant provision via `RolePermissionsSeed` (Sprint-9 U12). Without this, dropping class-level `[Authorize(Roles="Owner")]` on `AuthAdminController` could lock the only Owner user out of the admin surface.
- **JwtBearer is wired in `AddShopFlowDefaults`.** Sprint-9 U7 lifted JwtBearer to the kernel. Sprint-10 assumes any request that reaches a covered controller has passed through the JwtBearer middleware, and where authenticated, has a populated `perm[]` claim.
- **The covered-controller list is exhaustive as of Sprint-9.5 sign-off.** Verified during Phase 1.1 scan: 15 controllers total; 4 controller groups carry the covered surface (Inventory × 3, Outbound `OrdersController`, Inbound `PurchaseOrdersController`, Auth `AuthAdminController`).
- **`OrdersController.MarkPickFailed` maps to `outbound.orders.pick-confirm`** rather than `outbound.orders.cancel`. It is the pick-operator's failure-reporting action and lives in the pick-flow lifecycle; the cancellation it triggers is a downstream saga consequence, not the operator's own intent. The brainstorm names this expectation explicitly so planning does not have to invent it.
- **`OrdersController.POST /orders/seed` (DEV-only seeder) maps to `outbound.orders.write`.** No special permission key for the seeder. The DEV-only guard remains in place via the existing 404 / `environment_not_dev` response when `IHostEnvironment.IsDevelopment()` is false (Sprint-7 KTD).
- **`outbound.orders.cancel` has no current controller action to attach to.** Verified: `OrdersController` has 10 actions (Create / GetDetail / List / GetKpis / GetTransitions / Seed / ConfirmPick / MarkPickFailed / ConfirmPack / ConfirmShip). No Cancel action exists.

---

## Outstanding Questions

### Resolve Before Planning

(None — the dialogue resolved scope and the catalog provides clear action-to-key mappings for the planner.)

### Deferred to Planning

- [Affects R6][Technical] Where does the new reflection-based unit test class live? `tests/ShopFlow.SharedKernel.UnitTests/` (where the existing `PermissionKeysTests` + `PermissionPolicyCompositionTests` already live) is the natural home, but per-module test projects (Inventory.UnitTests, Auth.UnitTests, Outbound.UnitTests, Inbound.UnitTests) are also reasonable. Planner picks.
- [Affects R1][Technical] The exact action-to-key mapping for ambiguous cases. Most actions are obvious (`GetSummary` → `inventory.read`, `CreateSku` → `inventory.skus.write`, all 9 AuthAdmin actions are 1:1 with their AuthAdmin keys). A few are judgement calls — e.g., `OrdersController.GetTransitions` could attach to `outbound.orders.read` (sub-query of one order's audit) or `outbound.orders.pick-confirm` (mostly consumed by pick-flow UI). Planner reads the existing UI consumer in [`web/src/`](../../web/src) and assigns.
- [Affects R6][Technical] The reflection test's controller-discovery mechanism — explicit list of `Type` references (compile-time-checked; requires test edit when a new covered controller arrives) versus convention-based assembly scan (looser; auto-includes new covered controllers). Planner picks based on how often the covered-controller list is expected to change.
