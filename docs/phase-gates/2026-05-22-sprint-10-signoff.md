---
title: "Sprint-10 sign-off — Backend [Authorize(Policy=...)] migration"
date: 2026-05-22
status: complete
follows: docs/phase-gates/2026-05-21-sprint-9.5-signoff.md
plan: docs/plans/2026-05-22-001-feat-sprint-10-backend-authorize-policy-migration-plan.md
origin: docs/brainstorms/2026-05-22-sprint-10-backend-authorize-policy-migration-requirements.md
tag: v0.14.0-sprint-10
---

# Sprint-10 sign-off — Backend `[Authorize(Policy=...)]` migration

Sprint-10 flips the backend authorization gate from class-level `[Authorize]` / `[Authorize(Roles="Owner")]` to per-action `[Authorize(Policy = PermissionKeys.X)]` across the four catalogued business-module controller groups. Sprint-9 catalogued the 24 keys + registered the policies; Sprint-9.5 aligned the frontend; Sprint-10 closes the loop so the frontend `usePerm` gate is now backed by a backend gate consulting the same `perm[]` claim. One verified-missing security gap (Inbound.Api had neither `UseAuthentication` nor `UseAuthorization` middleware) closed as a side-effect of U3. No new keys, no frontend changes, no integration tests beyond the per-module reflection-based unit tests — those park for Sprint-10.5 alongside the frontend per-button gating, the `hub.connect` application, and the frontend `admin.ts` `PERMISSION_KEYS` catalog drift fix.

## What shipped

| U-ID | Goal | Status | Commit |
|------|------|--------|--------|
| U0 | Branch cut from v0.13.0-sprint-9.5 + brainstorm + plan + 8 KTDs in opening commit body | ✅ | `742854e` |
| U1 | Inventory: 8 actions across 3 controllers (`InventoryController` ×1, `SkusController` ×6, `AdjustmentsController` ×1) — per-action `[Authorize(Policy=...)]`; class-level `[Authorize]` removed; `InventoryAuthorizePolicyCoverageTests` reflection test (75 tests: 74 passed + 1 illustrative skip) | ✅ | `dacf767` |
| U2 | Outbound `OrdersController`: 10 actions — per-action policies; class-level `[Authorize]` removed; orphan-key `OutboundOrdersCancel` documented (KTD6); `OutboundAuthorizePolicyCoverageTests` (130 tests: 129 passed + 1 illustrative skip); Sprint-7 dual-ctor pattern untouched | ✅ | `83b3b4e` |
| U3 | Inbound three-part change: (a) `Program.cs` gains `UseAuthentication()` + `UseAuthorization()` between `UseProblemDetails()` and `UseTenantRouting()` (KTD3 — verified missing); (b) `PurchaseOrdersController` 6 actions per-action policies; (c) `InboundAuthorizePolicyCoverageTests` (29 tests: 28 passed + 1 illustrative skip). Pre-flight grep of `tests/ShopFlow.Inbound.IntegrationTests/` confirmed no anonymous-200 WebApplicationFactory tests exist | ✅ | `57e4dc2` |
| U4 | Auth `AuthAdminController`: 9 actions per-action policies (1:1 with `PermissionKeys.OwnerCritical`); class-level `[Authorize(Roles="Owner")]` removed; XML doc-comment rewritten to reference KTD13 + RolePermissionsSeed safety nets; `AuthAdminAuthorizePolicyCoverageTests` with KTD5 dual-pin test asserting AuthAdmin policy set equals `OwnerCritical` (186 tests: 185 passed + 1 illustrative skip) | ✅ | `1256b22` |
| U5 | Sign-off (this doc) + Auth AGENTS.md update + README current-stage + CLAUDE.md current-stage + CHANGELOG entry + tag `v0.14.0-sprint-10` | ✅ | (this commit) |

## Architecture Summary

**Per-action policies are the single canonical gate (KTD5).** Class-level `[Authorize]` and `[Authorize(Roles="Owner")]` are fully dropped on the four covered controller groups — no belt-and-braces. The safety nets that catch any future-edit drift are:

1. **`RolePermissionsSeed`** (Sprint-9 U12) reads `PermissionKeys.All` reflectively and inserts every key for the Owner role at every tenant provision — Owner never gets locked out, even if a future plan body forgets to seed a new key.
2. **`OwnerCritical` server-side guard** (Sprint-9 U8 KTD13) in `RolePermissionsCommandHandler` rejects any `UpdateRolePermissions` request that would leave the Owner row missing any `PermissionKeys.OwnerCritical` entry. Server-side enforced; not client-trusted.
3. **U4 reflection test KTD5 dual-pin** asserts the 9 keys attached to `AuthAdminController` actions form a set equal to `PermissionKeys.OwnerCritical`. Drift in either direction (an admin key removed from `OwnerCritical`, or a new admin action with a key not in `OwnerCritical`) fails the test.

**Per-module reflection tests live in `tests/ShopFlow.<Module>.UnitTests/Authorization/` (KTD1).** Four new test classes, one per module — `InventoryAuthorizePolicyCoverageTests`, `OutboundAuthorizePolicyCoverageTests`, `InboundAuthorizePolicyCoverageTests`, `AuthAdminAuthorizePolicyCoverageTests`. Each one references `PermissionKeys.X` constants directly (KTD2) so a catalog rename surfaces as a compile error rather than a runtime test failure. Each shipped with `<ProjectReference>` from the UnitTests csproj to the matching Api csproj (Auth.UnitTests already carried it from Sprint-9 U6). CI's per-csproj matrix surfaces drift earlier than a single cross-cutting test in SharedKernel.UnitTests would.

**Inbound.Api authentication posture changed (KTD3).** Before U3, `Inbound.Api/Program.cs` had NEITHER `app.UseAuthentication()` NOR `app.UseAuthorization()` — verified by reading the file at plan time. `PurchaseOrdersController` had no `[Authorize]` either. Result: Inbound POs were *unauthenticated in production*. U3 inserts both middleware lines between `UseProblemDetails()` and `UseTenantRouting()`, matching Inventory.Api / Outbound.Api ordering. Behaviorally safe because no production frontend currently calls Inbound endpoints (frontend `/inbound` route is the Sprint-6 `ComingSoon` placeholder); pre-flight grep of `tests/ShopFlow.Inbound.IntegrationTests/` confirmed no test asserts anonymous-200 against `PurchaseOrdersController`. Inbound.Api keeps hand-wired auth middleware rather than migrating to `UseShopFlowSecurityPipeline()` (KTD4 — bundling that migration would expand scope; cross-business-module consistency with Inventory / Outbound preserved).

**Catalogued-but-unapplied keys stay in `PermissionKeys.All` (KTD6).** `OutboundOrdersCancel` (`outbound.orders.cancel`) and `HubConnect` (`hub.connect`) remain in the catalog at Sprint-10 sign-off. 22 of 24 keys are applied to actions; 2 wait for their attachment surface (a future `CancelOrder` action on `OrdersController`; the `hub.connect` policy on `TenantHub` in Sprint-10.5). Removing the keys would force key-removal commits for orphans (opposite direction); adding stub actions just to attach them would be fake coverage. The reflection tests do NOT enforce "every catalogued key has at least one application." U2's `OutboundOrdersCancel_RemainsCataloguedButUnapplied` test asserts the orphan key still exists in `PermissionKeys.All` so a sloppy edit removing it fails CI.

**AuthController self-service endpoints unchanged (KTD7).** Logout / me-password / mfa-enroll-begin / mfa-disable / mfa-recovery-codes keep their bare `[Authorize]` attribute. Authentication is necessary and sufficient for own-account actions; no perm keys exist for "logout self" or "change my own password" by design.

## Key Technical Decisions

1. **KTD1 — Per-module reflection tests in `tests/ShopFlow.<Module>.UnitTests/Authorization/`** rather than a single cross-cutting test in `ShopFlow.SharedKernel.UnitTests/`. Test layout mirrors source per AGENTS.md §81; CI per-csproj matrix surfaces drift earlier; less cross-module `<ProjectReference>` coupling than the alternative.
2. **KTD2 — Reflection tests reference `PermissionKeys.X` constants directly** (not string literals, not `nameof()`). A catalog rename surfaces as a compile error in the test.
3. **KTD3 — Inbound.Api Program.cs gains `UseAuthentication()` + `UseAuthorization()` in U3.** Verified-missing middleware; per-action policies would silently no-op without it. Side-effect closure: Inbound POs become authenticated-by-default after Sprint-10.
4. **KTD4 — Inventory.Api + Outbound.Api + Inbound.Api keep hand-wired auth middleware.** Migration to `UseShopFlowSecurityPipeline` is a separate cleanup; bundling it into Sprint-10 would expand scope.
5. **KTD5 — Class-level `[Authorize]` and `[Authorize(Roles="Owner")]` are FULLY DROPPED.** Per-action policies are the single canonical gate. Safety nets: `RolePermissionsSeed` + `OwnerCritical` server-side guard + U4 reflection test dual-pinning the 9 AuthAdmin keys equal `PermissionKeys.OwnerCritical`.
6. **KTD6 — Catalogued-but-unapplied keys stay in `PermissionKeys.All`** at Sprint-10 sign-off. 22 of 24 keys applied; 2 wait for their attachment surface.
7. **KTD7 — `AuthController` self-service endpoints keep bare `[Authorize]`.** Authentication is necessary and sufficient for own-account actions.
8. **KTD8 — Canonical action-to-key mapping table (33 actions across 6 controllers in 4 controller groups).** Full table in the plan document.

## Deviations from plan

1. **`*Async` method name suffix used in source.** Plan KTD8 referenced action names like `Create` / `GetById` / `ListOpen` / `ConfirmPick`. The actual source files declare them with the `Async` suffix (`CreateAsync`, `GetByIdAsync`, `ListOpenAsync`, `ConfirmPickAsync`, etc.) — verified by the subagents reading the controllers first. Per-action mappings preserved unchanged; reflection tests assert against the `Async` method names. Documented inline in U2 / U3 commit bodies.
2. **U4 csproj — no change needed.** Plan said `tests/ShopFlow.Auth.UnitTests/ShopFlow.Auth.UnitTests.csproj` would gain a `<ProjectReference>` to `ShopFlow.Auth.Api`. The reference was already in place from Sprint-9 U6. U4 ships the new test file only.
3. **Inbound.Api `Program` class accessibility** (plan deferred Q2) not changed. Plan said "implementer picks" whether to expose `Program` as `public partial class Program` for future `WebApplicationFactory<Program>` integration tests. U3 deferred — the middleware change is independent of the test-fixture posture, and no Sprint-10 work needs the partial class. Sprint-10.5 can add it when integration tests against Inbound endpoints land.
4. **`docs/solutions/` entries not added.** Plan U5 mentioned no required solutions notes; Sprint-10 work was direct execution of an already-explained design. The XML doc-comment on `AuthAdminController` carries the on-disk reference to KTD13 + `RolePermissionsSeed` for future maintainers.
5. **`csharpier` reformatted touched files in place.** Subagents ran `dotnet csharpier format` on touched files post-edit. OrdersController.cs picked up substantial whitespace reflow as a side-effect of attribute insertion through a file that had drifted from CSharpier's preferred shape; the diff is larger than the semantic change but is verified equivalent.
6. **Push to origin deferred.** Plan U5 said "push branch + tag to origin per memory feedback `push-before-phase-switch`." Sprint-10 sign-off (this doc + tag) is the immediate next step; push happens immediately after tagging.

## Verification

- **Build**: `dotnet build ShopFlow.sln` → **0 errors + 0 warnings** across 47 projects (R9 satisfied). Verified after each unit and at sign-off.
- **Inventory tests**: `dotnet test tests/ShopFlow.Inventory.UnitTests/` → **74 passed + 1 skipped** (illustrative). Sprint-9.5 baseline preserved (R10).
- **Outbound tests**: `dotnet test tests/ShopFlow.Outbound.UnitTests/` → **129 passed + 1 skipped**. Sprint-7 OrdersController integration tests carry forward unchanged.
- **Inbound tests**: `dotnet test tests/ShopFlow.Inbound.UnitTests/` → **28 passed + 1 skipped**. Sprint-2-redux Inbound integration tests do not regress.
- **Auth tests**: `dotnet test tests/ShopFlow.Auth.UnitTests/` → **185 passed + 1 skipped** (was 173 at Sprint-9.5 baseline; +12 from the U4 reflection test class).
- **Reflection contract** (all four `Authorization/<Module>AuthorizePolicyCoverageTests.cs`): every public action on covered controllers carries `[Authorize(Policy = PermissionKeys.X)]`; X is in `PermissionKeys.All`; covered controllers have no class-level `[Authorize]` / `[Authorize(Roles=...)]`. Per-action `[Fact]` so a mismatch fails the specific action's test, not all at once. Illustrative `[Fact(Skip=...)]` per file documents the negative-path shape.
- **CSharpier**: Husky pre-commit hook ran on each commit; all touched files passed.

## Trade-offs Carried Forward

1. **Per-action `usePerm()` button gating across Inventory / Orders / Inbound business pages** — Sprint-10.5. Pattern shipped via Sprint-9.5 routeGuard.ts + Sidebar; per-component wrapping is incremental.
2. **`hub.connect` policy application to `TenantHub`** — Sprint-10.5. The key is catalogued (KTD6); the attachment surface is in SharedKernel + Outbound.Api (single hub-host topology per Sprint-7 KTD).
3. **403 wire-shape integration tests** — Sprint-10.5. Sprint-10 trusts the policy mechanism contract pinned by Sprint-9 `PermissionPolicyCompositionTests`; runtime 403 assertions on covered endpoints with valid-JWT-missing-key requests land in Sprint-10.5's Docker-backed suite.
4. **Frontend `web/src/api/admin.ts` `PERMISSION_KEYS` catalog drift** — Sprint-10.5. Plan Phase-1 research surfaced ~9 of 12 frontend strings out of sync with backend `PermissionKeys.All` (`outbound.orders.confirm-pick` vs backend `outbound.orders.pick-confirm`; `inventory.skus.create` / `.update` vs single `inventory.skus.write`; `inbound.pos.create` vs `inbound.pos.write`; `inbound.receiving.confirm` vs `inbound.receive.confirm`; `hub.tenant.read` / `hub.tenant.write` vs `hub.connect`; `notification.dlq.read` doesn't exist; `outbound.orders.mark-pick-failed` doesn't exist). Sprint-9.5 U7 `RolePermissionsEditor` ships against stale strings — invisible today because only Owner is provisioned and Owner has all keys. Sprint-10.5 must align the frontend catalog AND add a contract test pinning the alignment.
5. **`CancelOrder` action on `OrdersController`** — future sprint. Or remove `outbound.orders.cancel` from `PermissionKeys.All` when the canonical decision lands that Outbound never gets a direct Cancel surface (compensation flows via saga, not REST).
6. **Inventory.Api + Outbound.Api + Inbound.Api migration to `UseShopFlowSecurityPipeline()`** — future sprint. KTD4 keeps hand-wired auth middleware across the three business modules for cross-module consistency.
7. **`Program` partial-class exposure on Inbound.Api** — Sprint-10.5 if integration tests against Inbound HTTP endpoints land.
8. **Per-permission attribute migration across the remaining ungated live controllers** (`ChannelController`, `ProductMappingsController`, `SkuFlagsController`, `SyncStateController`, `PutAwayController`) — requires new permission keys; deliberately out of Sprint-10 scope per the brainstorm Scope Boundaries.

## Sprint-10 KTDs

KTD1-KTD8 captured in the plan body (Sprint-10 U0 commit `742854e`) and re-stated above. The full action-to-key mapping table (KTD8 — 33 actions × 4 controller groups) lives in the plan at [docs/plans/2026-05-22-001-feat-sprint-10-backend-authorize-policy-migration-plan.md](../plans/2026-05-22-001-feat-sprint-10-backend-authorize-policy-migration-plan.md).

## Next implementation step (post-tag)

Cut a fresh branch from `v0.14.0-sprint-10` and start one of:

- **Sprint-10.5** — Bundled trade-off closures: per-button `usePerm()` gating across Inventory / Orders / Inbound business pages + `hub.connect` policy attachment on `TenantHub` + 403 wire-shape Docker-backed integration tests + frontend `web/src/api/admin.ts` `PERMISSION_KEYS` catalog drift fix + role-mapping pin contract test. Same point-release cadence as Sprint-2.5 / 4.5 / 7.5 / 8.5 / 9.5.
- **Sprint-11** — First multi-role surface (picker or operations dispatcher). Provisions a Picker role with a deliberately narrower `perm[]` set than Owner; first end-to-end test that the per-permission gating actually rejects unauthorized requests. Backend already in place; Sprint-9.5 frontend Sidebar + route guard + RolePermissionsEditor already understand non-Owner roles via UI gating.
- **Sprint-9.6 polish** — a11y harness extension across the 9 Sprint-9.5 components (5 auth screens + RecoveryCodesDisplay + 3 admin pages); ProfileSecurityScreen `useMe()` migration; `/admin/users` TanStack Query migration with cache invalidation.
- **Phase-3 polish** — Observability dashboards (notification_outbox depth + dead_letter count + auth-failures-per-tenant + refresh-rotations-per-second + per-permission denial rates per tenant); `auth_audit_log` partitioning + archival; KMS/Vault migration of TOTP KEK; `CREATE INDEX CONCURRENTLY` review before first true production deploy.
