---
date: 2026-05-22
topic: sprint-11-first-multi-role-surface
---

# Sprint-11 — First Multi-Role Surface (Single Picker)

## Summary

Provision the first non-Owner role on ShopFlow WMS and exercise the defense-in-depth stack end-to-end under a real narrowed `perm[]` set. Sprint-11 ships a Single Picker role with a 4-key baseline (`outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`) pre-seeded into `role_permissions` at every tenant provision; Owner creates Picker users via the existing `/admin/users` page (Sprint-9.5 U7); Picker logs in via the standard Sprint-9 auth flow and sees the existing Orders + Inventory pages with Sprint-10.5 U5 control gates hiding every write surface except ConfirmPick + MarkPickFailed; a Docker-backed end-to-end happy-path integration test pins the chain Owner creates Picker → Picker logs in → ConfirmPick advances the saga → audit log records the action. Multi-role workflow hand-off (Dispatcher + Owner-Picker-Dispatcher pipeline), dedicated picker-queue UI, force-change-on-first-login, and Picker MFA enforcement defer to Sprint-12+. The deliverable proves that Sprint-9.5 + Sprint-10 + Sprint-10.5's RBAC stack actually rejects unauthorized requests under a real non-Owner JWT and delivers the workflow value of role separation.

---

## Problem Frame

Sprint-9 catalogued 24 permission keys, registered one ASP.NET policy per key, and emitted them on every JWT as a JSON `perm[]` claim. Sprint-10 attached per-action `[Authorize(Policy=...)]` to 33 actions across four controller groups. Sprint-10.5 extended Sprint-9.5's `usePerm` hook to 7 interactive controls + 1 modal-render path on Inventory + Orders pages, attached `hub.connect` to `TenantHub`, pinned the frontend catalog set-equality contract, and shipped 33 + 1 Docker-backed 403 wire-shape integration tests.

But no non-Owner role has been provisioned. Every tenant Owner today carries all 24 keys (Sprint-9 U12 `RolePermissionsSeed` bootstraps Owner with the full set reflectively); the perm-gate + control-gate + backend-policy stack works in tests against synthetic narrowed JWTs but has never been exercised under a real role configuration where a user logs in, navigates the system, performs work, and gets rejected on actions they cannot perform. The defense-in-depth stack is theoretical until Sprint-11 provisions Picker.

The Sprint-10 sign-off, Sprint-10.5 sign-off, and three preceding sprints all named Sprint-11's first multi-role surface as the natural next sprint — the precondition was the comprehensive backend + frontend RBAC stack landing. That stack is in place; Sprint-11 demonstrates it works for a real user.

**Why Single Picker first**: a single new role with a narrow perm[] is the smallest meaningful unit that proves the stack works end-to-end. Dispatcher + multi-role workflow hand-off (Owner creates PO → Picker confirms pick → Dispatcher confirms pack/ship) is bigger scope and shows up in Sprint-12 once Picker proves valuable. The single-role experiment also surfaces any frictions in `/admin/users` + `RolePermissionsEditor` + Sidebar perm-filter + Sprint-10.5 U5 control gates that a comprehensive Sprint-9.5 U7 walkthrough may have missed because no production-style role configuration tested them.

**Why gated existing UI, not dedicated picker queue**: Sprint-10.5 U5 made an explicit design bet that comprehensive control-level gating replaces the need for role-specific UI variants. Picker hits `/orders`, sees the orders dashboard read-only, sees ConfirmPick + MarkPickFailed buttons on orders in AwaitingPick state. No new routes; no dedicated picker view. Sprint-11 validates the bet pays off; a dedicated picker queue lands in Sprint-12+ if real users find the gated existing UI insufficient.

---

## Requirements

### Role + permission seeding

- **R1.** A new Picker role exists in the system with a canonical 4-key `perm[]` baseline: `outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`. The role name `Picker` is already valid in the Sprint-9 `UserRole` enum + Sprint-9.5 U7 admin.ts `EditableRole` type.
- **R2.** `RolePermissionsSeed` (Sprint-9 U12) is extended to also insert a Picker row in `role_permissions` with the 4-key baseline at every tenant provision. Idempotent (ON CONFLICT DO NOTHING) — re-running the seed on an existing tenant does not duplicate or overwrite Owner-customized Picker keys. Owner can still edit Picker keys later via Sprint-9.5 U7 `/admin/role-permissions` (KTD13 OwnerCritical guard does NOT apply to Picker; Picker keys are freely editable by Owner).

### Provisioning + authentication

- **R3.** Owner creates Picker users via the existing Sprint-9.5 U7 `/admin/users` page → POST `/api/auth/admin/users` with `role=Picker`. No new provisioning code paths shipped in Sprint-11. The endpoint already handles the temp-password generation; Owner copies the temp password from the UI and shares it with the Picker out-of-band.
- **R4.** Picker logs in via the standard Sprint-9 `/api/auth/login` endpoint with their temp password. The login response is the Sprint-9 + Sprint-9.5 standard shape (no MFA challenge for Picker — MFA is not enforced; see Scope Boundaries). The issued JWT carries `role=Picker` + `perm[]` containing the 4 keys (read from `role_permissions` via the existing Sprint-9 U6 `JwtTokenIssuer` flow).
- **R5.** Picker can change their password at any time via Sprint-9.5 U6 `/_auth/profile/security` (`/api/auth/me/password`). Sprint-11 does NOT enforce force-change-on-first-login; Picker can technically operate on the temp password indefinitely until they choose to change it (portfolio-tier security trade-off; production hardening lands in a future sprint).

### UI behavior under Picker JWT

- **R6.** Sidebar (Sprint-9.5 U8 `permRequired` filter) under Picker JWT shows Orders + Inventory nav items only. Hides Inbound (Picker lacks `inbound.pos.read`); hides Admin nav group (Picker lacks any `auth.admin.*` keys). Hides Notification DLQ if present (Sprint-9.5 carry).
- **R7.** Picker hits `/orders` → existing Sprint-7 + Sprint-10.5 orders dashboard renders. KPI strip, order list, filters, detail route — all read-accessible. Write-bearing controls hidden per Sprint-10.5 U5 (`SeedTestOrderButton` hidden because Picker lacks `outbound.orders.write`).
- **R8.** Picker hits an order detail page in `AwaitingPick` state → ConfirmPick + MarkPickFailed buttons render (both gate on `outbound.orders.pick-confirm` per Sprint-10.5 U5 mapping). Order detail pages in other saga states (Reserved / AwaitingPack / Shipped / Cancelled) hide ConfirmPick + MarkPickFailed naturally because the buttons don't apply to those states.
  - **Note**: ConfirmPick + MarkPickFailed buttons are NOT in the current `_auth/orders/$orderId.tsx` route (Sprint-10.5 deviation note carried forward). Sprint-11 needs to add them as part of this requirement, OR explicitly defer them to a future sprint and ship Sprint-11's E2E test exercising the backend endpoint directly via HTTP. **Implementer-time decision**: see Outstanding Questions.
- **R9.** Picker hits `/inventory` → existing Sprint-6 + Sprint-7.5 inventory dashboard renders read-only. SKU table, KPI strip, ledger drawer all accessible. Write controls (AdjustStockModal trigger, CreateSkuModal trigger, EditSkuModal Edit button, ThresholdInlineEdit, FlashSaleToggle) all hidden per Sprint-10.5 U5 gates.

### Backend enforcement

- **R10.** Picker JWT against `POST /api/outbound/orders/{id}/confirm-pick` returns 200 + saga state advances to `AwaitingPack`. The action's `[Authorize(Policy = PermissionKeys.OutboundOrdersPickConfirm)]` (Sprint-10 U2) passes; the FulfillmentSaga (Sprint-3-redux) handles the state transition.
- **R11.** Picker JWT against `POST /api/outbound/orders/{id}/mark-pick-failed` returns 200 (same Sprint-10 KTD8 mapping to `OutboundOrdersPickConfirm`). The saga's pick-failure compensation handler engages (Sprint-3-redux U7).
- **R12.** Picker JWT against any of the 31 OTHER Sprint-10-migrated actions (i.e., all 33 minus ConfirmPick + MarkPickFailed) returns 403. This is largely already covered by Sprint-10.5 U4's 33 Docker tests (which use narrowed JWTs); Sprint-11 inherits that coverage and adds a small positive-case companion.
- **R13.** Picker JWT against `POST /hub/negotiate` succeeds (Picker holds `hub.connect`; Sprint-10.5 U3 policy passes). Real-time saga + stock-level events propagate to Picker via the existing Sprint-7 SignalR push channel.
- **R14.** Audit log: Picker's login + ConfirmPick action both write rows to Sprint-9 `auth_audit_log` (login flow) and the Sprint-7 `outbound_saga_transitions` table (saga state change). Sprint-11 does not modify the audit pipeline; it relies on the existing instrumentation.

### Verification

- **R15.** Docker-backed end-to-end integration test pins the happy-path chain. The test arranges: provision a tenant via `OwnerSeed` + `RolePermissionsSeed` (extended to include Picker baseline); seed an Owner + a Picker user; seed an order in `AwaitingPick` state. The test acts: Picker calls `/api/auth/login` with their temp password; receives JWT with the 4-key perm[]; calls `POST /api/outbound/orders/{id}/confirm-pick` with the JWT. The test asserts: login response 200 + JWT shape; pick-confirm response 200; saga state has advanced to `AwaitingPack` (reads `saga_state` table); audit log has new rows for the login + pick action. Lives in `tests/ShopFlow.Auth.IntegrationTests/Picker/PickerHappyPathTests.cs` (or equivalent path). Skip-marked locally per Sprint-1+ posture; CI runs the full Docker-backed suite.
- **R16.** Build + existing test suite gates. `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings across all 47 projects. Sprint-10.5 baselines preserved: 53 SharedKernel.UnitTests + 3 AdminTsCatalogContractTests + 87 frontend Vitest + 4 skipped.

---

## Acceptance Examples

- **AE1. Covers R1, R2.** Given a fresh tenant provisioned via `shopflow-migrate provision`, when `RolePermissionsSeed` runs, `role_permissions` contains a Picker row with exactly 4 keys: `outbound.orders.read`, `outbound.orders.pick-confirm`, `inventory.read`, `hub.connect`. Owner row unchanged (24 keys).
- **AE2. Covers R3, R4.** Given Owner logged into the admin UI, when Owner navigates to `/admin/users`, fills email = `picker@yensao.test` + role = Picker, and clicks Create, the page returns success + displays a one-time temp password. When Picker logs in via `/login` with that temp password, the issued JWT decodes to carry `role=Picker` + `perm[]` containing exactly the 4 keys from AE1.
- **AE3. Covers R6.** Given Picker is logged in, when Picker views the Sidebar, only Orders + Inventory nav items are present. Inbound + the Admin nav group (Users / Locked Accounts / Role Permissions) are absent.
- **AE4. Covers R7, R8.** Given Picker is logged in and an order in `AwaitingPick` state exists, when Picker navigates to `/orders/<id>`, the order detail page renders with SagaPipeline + TransitionsLog + OrderLineItems all visible. ConfirmPick + MarkPickFailed buttons are visible. AdjustStockModal trigger, CreateSkuModal trigger, EditSkuModal Edit, ThresholdInlineEdit, FlashSaleToggle (if cross-referenced) all hidden.
- **AE5. Covers R10.** Given Picker is logged in and an order in `AwaitingPick` state exists, when Picker submits `POST /api/outbound/orders/{id}/confirm-pick`, the response is 200. The order's saga state advances to `AwaitingPack` within the saga's normal processing window.
- **AE6. Covers R12.** Given Picker is logged in, when Picker submits `POST /api/v1/inventory/adjustments` (a Sprint-10-migrated endpoint requiring `inventory.adjust`), the response is 403. Same outcome for the other 30 endpoints in Sprint-10's covered set that Picker lacks the key for.
- **AE7. Covers R15.** Given the Sprint-11 E2E happy-path test class exists at `tests/.../Picker/PickerHappyPathTests.cs`, when CI runs the full Docker-backed integration suite, the test passes: tenant provisioned + Picker seeded + Owner-created → Picker login 200 + JWT shape verified → pick-confirm 200 + saga advanced + audit rows written.

---

## Success Criteria

- A non-Owner role works end-to-end under the defense-in-depth stack — login + UI navigation + action authorization all pass for actions in Picker's perm[] and fail with 403 for actions outside it.
- Sprint-10.5's `usePerm` (reactive) gates demonstrably hide/show the right controls under a real Picker JWT. Sprint-10.5 KTD3's choice (reactive over snapshot) gets exercised in production-style flow.
- Sprint-10.5 U4's 33 Docker 403 tests are inherited as Picker-rejection coverage; Sprint-11 adds the small positive-case companion for ConfirmPick + MarkPickFailed (the 2 endpoints Picker IS allowed to call).
- Sprint-9.5 U7 `/admin/users` + `RolePermissionsEditor` admin surfaces are proven load-bearing — they ARE the canonical provisioning + role-config flow.
- `RolePermissionsSeed` extension (R2) ships idempotent + tenant-provision-safe; subsequent runs against an existing tenant don't overwrite Owner-edits to Picker keys.
- Sprint-12 unblocked: Dispatcher role + multi-role workflow hand-off can land on top of Sprint-11's foundation with minimal new architecture. The same patterns (admin-page provisioning + RolePermissionsSeed extension + gated existing UI + E2E test) extend trivially.
- Audit log captures Picker login + ConfirmPick action via existing Sprint-9 `auth_audit_log` + Sprint-7 `outbound_saga_transitions` pipelines without code changes.

---

## Key Decisions

- **Single Picker over multi-role hand-off pipeline.** Owner + Picker + Dispatcher hand-off (Owner creates PO → Picker confirms pick → Dispatcher confirms pack/ship) is the eventual canonical workflow but requires 3 roles + distinct UI affordances + workflow stitching. Sprint-11 ships ONE role to prove the stack; Sprint-12+ adds Dispatcher + workflow hand-off if Sprint-11 demonstrates value.
- **Gated existing UI over dedicated picker queue.** Sprint-10.5 U5's design bet was that comprehensive control-level gating obviates the need for role-specific UI variants. Sprint-11 validates that bet — Picker hits the same `/orders` page Owner does, sees a narrower set of affordances. If real Picker users find the gated existing UI insufficient, a dedicated picker queue lands in Sprint-12+ as a refinement.
- **Standard 4-key Picker baseline.** `outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`. Read access to orders + inventory + real-time push, plus the one write action (ConfirmPick / MarkPickFailed via Sprint-10 KTD8). Minimal (3-key without inventory.read) would force LedgerDrawer to gracefully degrade — punted to a future sprint. Extended (+ inbound.pos.read) adds dead weight today since no Inbound UI consumes it. MFA-enforced Picker adds setup ceremony and is deferred.
- **Provisioning via Sprint-9.5 U7 `/admin/users` canonical path.** The admin page already shipped this flow; Sprint-11 exercises it for the first time under a real non-Owner outcome. No CLI extension (`shopflow-migrate seed-user`) — Sprint-12+ adds CLI for ops-driven bootstrapping if needed.
- **`RolePermissionsSeed` pre-seeds Picker baseline.** Owner can still edit Picker keys later via `/admin/role-permissions` (Sprint-9.5 U7) without losing the pre-seed safety net. Idempotent ON CONFLICT DO NOTHING preserves any Owner customizations across re-seeds. Alternative (empty Picker row → operator manually grants keys) was rejected because it adds setup friction to every new tenant.
- **Force-change-on-first-login deferred.** Picker logs in with Owner-issued temp password and can operate indefinitely on it until they manually change via `/profile/security`. Portfolio-tier security trade-off; production hardening lands in a future sprint with an explicit "must change on first login" flag + UI flow.
- **Picker MFA not enforced.** Sprint-9 R17 makes Owner MFA mandatory; Sprint-11 explicitly does NOT extend this to Picker. Sprint-11 ships without MFA enrollment for Picker — defer MFA mandate (per role) to a Sprint-12+ decision once Dispatcher + other roles are in the picture.

---

## Scope Boundaries

### In-sprint scope additions surfaced during brainstorm

- **`RolePermissionsSeed` extension with Picker 4-key baseline** (R2) — small extension to Sprint-9 U12 seed; idempotent + tenant-provision-safe.
- **Decision flagged for implementer-time resolution (R8)**: do we ADD ConfirmPick + MarkPickFailed buttons to `_auth/orders/$orderId.tsx` as part of Sprint-11 R8 (filling in the Sprint-10.5 KTD7 gap that these buttons don't exist today), OR do we ship Sprint-11's E2E test exercising the backend endpoints directly via HTTP and defer the UI buttons to a Sprint-12+ workflow sprint? The brainstorm permits both shapes; planning resolves which one Sprint-11 ships.
- **End-to-end happy-path integration test** (R15) + small positive-case companion to Sprint-10.5 U4 for ConfirmPick + MarkPickFailed (R12 inheritance + 2 positive tests).

### Carried from origin (unchanged)

- Owner role + 24-key superset preserved.
- Owner MFA invariant (Sprint-9 R17) unchanged.
- Sprint-9.5 U7 admin surface (admin/users, admin/locked-accounts, admin/role-permissions) — Sprint-11 consumes; no modifications.
- Sprint-10.5 U5 control gates — Sprint-11 inherits; no modifications.
- Sprint-9 password reset flow + Sprint-9.5 U6 `/_auth/profile/security` — Picker uses these as-is for self-service password change.

### Deferred to follow-up work

- **Sprint-12 — Dispatcher role + multi-role workflow hand-off**. Add Dispatcher to RolePermissionsSeed with `outbound.orders.pack-confirm` + `outbound.orders.ship-confirm` + reads. End-to-end Owner-Picker-Dispatcher workflow test. May include the 4 saga action buttons (ConfirmPick / MarkPickFailed / ConfirmPack / ConfirmShip) on `_auth/orders/$orderId.tsx` if Sprint-11 deferred them.
- **Dedicated picker queue UI** (`/picker` route, filter / sort / batch confirm) — if Sprint-11's gated existing UI proves insufficient for real Picker workflow.
- **Force-change-on-first-login flag + UI flow** — production-tier security hardening.
- **Picker MFA required-enrollment** — per-role MFA mandate decision; Sprint-12+ alongside Dispatcher policy.
- **ProfileSecurityScreen `useMe()` migration** (Sprint-9.6 carry).
- **`shopflow-migrate seed-user --role=Picker` CLI extension** — operator-runbook path; future sprint if needed.
- **Extended Picker (+ `inbound.pos.read`)** — when Inbound frontend ships and Picker needs visibility into incoming POs.
- **`RolePermissionsSeed` widening to non-Owner roles' baselines for other roles** — Dispatcher / Receiving / Ops baselines lands alongside each role's introduction.
- **`outbound.orders.cancel` orphan-key attachment surface** — unchanged from Sprint-10.5 carry; future sprint when CancelOrder action ships.
- **Phase-3 observability dashboards** — per-permission denial rates per tenant + auth_audit_log partitioning + KMS/Vault TOTP KEK; unchanged from Sprint-10.5 carry.

---

## Dependencies / Prerequisites

- **`v0.14.1-sprint-10.5`** as the cut-from tag. Branch + tag pushed to origin.
- **Sprint-10.5 U1 frontend `admin.ts` catalog** — Picker's 4 keys all exist in the corrected `PERMISSION_KEYS` (verified).
- **Sprint-10.5 U2 catalog contract test** — guards future drift; Sprint-11 doesn't modify the catalog.
- **Sprint-10.5 U3 `TenantHub` policy** — Picker holds `hub.connect`; SignalR negotiation succeeds.
- **Sprint-10.5 U4 33 + 1 Docker 403 tests** — already prove the 31 endpoints Picker lacks keys for reject correctly; Sprint-11 inherits as Picker-rejection coverage.
- **Sprint-10.5 U5 `usePerm` (reactive) gates** — 7 controls + 1 modal gate hide automatically when Picker's JWT lacks the key.
- **Sprint-10.5 U6 trade-off carry on SEC-001** — legacy tenants must have run `shopflow-migrate seed-owner` before deploying Sprint-11 (otherwise `role_permissions` lacks rows; Picker provisioning would fail). Sprint-11 inherits this prerequisite.
- **Sprint-9.5 U7 `/admin/users` + `RolePermissionsEditor`** — provisioning + role-config UI; Sprint-11 consumes both.
- **Sprint-9.5 U8 Sidebar `permRequired` filter + `requirePermission()` route guard** — Picker's narrowed JWT drives Sidebar visibility + admin route access denial.
- **Sprint-9 backend auth module** — JWT issuance + Auth.Api endpoints + `auth_audit_log` instrumentation; Sprint-11 consumes.
- **Sprint-9 U12 `RolePermissionsSeed`** — Sprint-11 extends with Picker baseline; the seed runs at every tenant `provision`.
- **Sprint-7 + Sprint-3-redux `FulfillmentSaga` + `outbound_saga_transitions`** — the saga that ConfirmPick advances + the audit table.
- **Sprint-9 `UserRole` enum + Sprint-9.5 U7 `EditableRole` type** — both already recognize `Picker` as a valid role string.
- **Docker-backed CI test fixtures** — the E2E happy-path test runs in CI's existing Docker-backed nightly + per-PR job; Skip-marked locally per Sprint-1+ posture.

---

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `RolePermissionsSeed` Picker extension overwrites Owner-customized Picker keys on re-seed. | Low | Medium | ON CONFLICT DO NOTHING on the unique (role_name, tenant) key — only inserts when the row is absent. Re-running `shopflow-migrate provision` against an existing tenant does not modify existing role_permissions rows. Sprint-11 R2 explicitly mandates this idempotency contract. |
| Picker's temp password lifetime is unbounded (no force-change-on-first-login). | Medium | Low (portfolio-tier) | Acknowledged trade-off (deferred to future sprint). Picker can self-change via `/profile/security`; Owner can reset via `/admin/users`. For production-tier deployments, force-change-on-first-login lands as a follow-up. |
| ConfirmPick + MarkPickFailed buttons don't exist in `_auth/orders/$orderId.tsx` today (Sprint-10.5 KTD7 carry). Sprint-11 R8 demands they render visible for Picker on AwaitingPick orders. | Medium | Medium | Implementer-time decision flagged: either add the buttons in Sprint-11 (filling the gap) OR defer to Sprint-12 and ship Sprint-11's E2E test against the HTTP endpoint directly. Both shapes are permitted; planning resolves. |
| Picker hits LedgerDrawer (via SKU-line-item click on order detail) and inventory.read is sufficient to render the drawer but inventory.adjust hidden — UX might feel broken (drawer renders but no Adjust button). | Low | Low | Sprint-10.5 U5 DL-005 FYI already flagged this as a known UX shape for gated users; the drawer renders correctly read-only and the AdjustStockModal trigger is hidden naturally. Acceptable behavior. |
| Picker JWT lacks `auth.admin.*` keys — Sidebar correctly hides Admin nav, BUT if Picker somehow navigates to `/admin/users` directly via URL bar, requirePermission() route guard (Sprint-9.5 U8) should redirect. | Low | Medium | Sprint-9.5 U8 routeGuard.test verifies the route-guard fail-closed pattern. Sprint-11 inherits this protection without code change. AE6 verifies Sidebar visibility; an additional E2E test verifying URL-bar-direct access to /admin/users redirects under Picker JWT could be added (implementer-time decision). |
| Sprint-11 E2E test depends on a Sprint-10.5 U4 fixture pattern (`NarrowedJwtBuilder` via MSBuild link). The test boots Auth.Api's WebApplicationFactory<Program> + Outbound.Api WAF — two hosts. | Low | Medium | Sprint-10.5 U4 already proved 4 module fixtures work in parallel (Inventory + Outbound + Inbound + Auth) under Testcontainers Postgres. Sprint-11's E2E test reuses the existing infrastructure. If the test needs to span Auth + Outbound boots, the existing pattern composes. |
| Picker hub.connect succeeds via Sprint-10.5 U3 but the SignalR push delivers no events because no other actor is performing saga state changes during the E2E test. | Low | Low | The E2E test isn't required to verify push delivery; that's separate (Sprint-7 SignalR coverage already exists). R13 verifies the negotiation passes, not the push delivery. Real-time push under Picker JWT is verified through the existing Sprint-7 useSignalR contract tests. |

---

## Outstanding Questions

### Resolve Before Planning

- None — all 6 product decisions made during brainstorm dialogue.

### Deferred to Implementation

- **[Affects R8]** Whether to add ConfirmPick + MarkPickFailed buttons to `_auth/orders/$orderId.tsx` as part of Sprint-11 (fills the Sprint-10.5 KTD7 gap; ~15-30 minutes of work to wire the buttons + their action handlers) OR defer to Sprint-12 and ship Sprint-11's E2E test exercising the backend endpoint via direct HTTP (no UI surface for the action). Planning picks; the brainstorm permits both.
- **[Affects R15]** Exact location of the E2E test (`tests/ShopFlow.Auth.IntegrationTests/Picker/PickerHappyPathTests.cs` vs `tests/ShopFlow.Outbound.IntegrationTests/Picker/` vs a new cross-cutting `tests/ShopFlow.Picker.IntegrationTests/`). The test crosses Auth + Outbound module surfaces; placement decision is implementer-time.
- **[Affects R3, R15]** Picker test-user email convention (`picker@<tenant>.test` vs a more realistic shape). Implementer picks during test fixture authoring.
- **[Affects R10]** Exact saga state assertion shape: assert the saga's state field directly via DB read after pick-confirm + a polling-with-timeout wait OR rely on the saga's synchronous-by-default in-test configuration. The saga's MT TestHarness is the canonical pattern (Sprint-3-redux).
- **[Affects R6]** Whether to add a frontend Vitest test asserting "Sidebar under Picker JWT shows the expected narrowed nav" OR rely on Sprint-9.5 U8's existing Sidebar `permRequired` filter tests as sufficient coverage. The Sprint-9.5 tests use synthetic narrowed JWTs; a Picker-specific test would be redundant. Implementer's call.

Each of these is execution-time discovery — answerable by reading code or running grep at the moment of implementation, not by additional brainstorm research.
