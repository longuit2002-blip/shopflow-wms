---
title: "Sprint-11 — First Multi-Role Surface (Single Picker)"
date: 2026-05-22
status: active
depth: standard
type: feat
origin: docs/brainstorms/2026-05-22-sprint-11-first-multi-role-surface-requirements.md
---

# Sprint-11 — First Multi-Role Surface (Single Picker)

## Summary

Ship the first non-Owner role on ShopFlow WMS — a Single Picker with a 4-key `perm[]` baseline (`outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`) pre-seeded into `role_permissions` at every tenant provision via a `RolePermissionsSeed` extension. Owner creates Picker users via the existing Sprint-9.5 U7 `/admin/users` page; Picker logs in via the standard Sprint-9 auth flow and sees the existing Orders + Inventory pages with Sprint-10.5 U5 control gates hiding every write surface except ConfirmPick + MarkPickFailed — those 2 buttons land in this sprint on `_auth/orders/$orderId.tsx` (filling the Sprint-10.5 KTD7 deferred gap per the plan-time R8 decision; brainstorm explicitly deferred the buttons-vs-defer choice to planning). `useOrderMutations` (Sprint-7) gains 2 new mutation hooks preserving the Idempotency-Key + toast + invalidation discipline. A Docker-backed end-to-end happy-path integration test at `tests/ShopFlow.Outbound.IntegrationTests/Picker/PickerHappyPathTests.cs` pins the chain Owner-creates-Picker → Picker-logs-in → ConfirmPick-advances-saga → audit-log-records-action. Saga state advance verified via poll-with-timeout DbContext read against `outbound_saga_transitions`. Skip-marked locally per Sprint-1+ posture; CI runs the full Docker-backed suite. Tagged `v0.15.0-sprint-11` (minor bump matching Sprint-9.5 precedent — Sprint-11 introduces a new role + UI surfaces, not trade-off closure).

---

## Problem Frame

Carried from the [origin brainstorm](../brainstorms/2026-05-22-sprint-11-first-multi-role-surface-requirements.md). In short: Sprint-9 catalogued 24 permission keys + ASP.NET policies + JWT `perm[]` claim. Sprint-10 attached per-action policies to 33 actions. Sprint-10.5 extended Sprint-9.5's `usePerm` to 7 controls + 1 modal gate, attached `hub.connect` to `TenantHub`, pinned the catalog set-equality contract, and added 33+1 Docker-backed 403 wire-shape tests. But no non-Owner role has been provisioned. Today's Owner carries all 24 keys via `RolePermissionsSeed` (Sprint-9 U12); the defense-in-depth stack is theoretical until Sprint-11 exercises it under a real narrowed `perm[]` set.

**Plan-time research findings (load-bearing for the plan)**:

1. **`RolePermissionsSeed` exists at `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs`** (Sprint-9 U12). Today it inserts a single Owner row with all 24 keys via reflection over `PermissionKeys.All`. Sprint-11 extends with a Picker baseline (4 keys). Idempotent via `ON CONFLICT DO NOTHING` on the unique `(role_name, tenant)` constraint (verify exact constraint name at U1 implementation time). Smoke-tested in `tests/ShopFlow.Migrate.UnitTests/Provisioning/RolePermissionsSeedTests.cs` + integration-tested in `tests/ShopFlow.Migrate.IntegrationTests/Provisioning/`.

2. **`Picker` role string is already recognized by the system**: Sprint-9 `UserRole` enum + Sprint-9.5 U7 admin.ts `EditableRole = 'Picker' | 'Dispatcher'`. No new enum value needed.

3. **`useOrderMutations` exists at `web/src/hooks/useOrderMutations.ts`** (Sprint-7). Today it ships the `seedOrder` mutation hook with Idempotency-Key + toast + invalidation discipline. Sprint-11 extends with `confirmPick` + `markPickFailed` hooks following the same pattern. Tests live at `web/src/hooks/useOrderMutations.test.tsx`.

4. **Order detail route at `web/src/routes/_auth/orders/$orderId.tsx`** (Sprint-7 U13) renders SagaPipeline + TransitionsLog + OrderLineItems but currently has NO ConfirmPick / MarkPickFailed action buttons (Sprint-10.5 U5 deviation surfaced this). Sprint-11 U2 adds them.

5. **Sprint-10.5 U4 `OutboundAuthorizationFixture` at `tests/ShopFlow.Outbound.IntegrationTests/Authorization/OutboundAuthorizationFixture.cs`** ships a `WebApplicationFactory<Program>`-backed Outbound.Api host with Testcontainers Postgres + Auth:DevSecret override + `NarrowedJwtBuilder` access. Sprint-11 U3 extends or composes with this fixture for the E2E test (needs Auth.Api login flow + Outbound.Api pick-confirm endpoint + DbContext access to query saga state).

6. **`NarrowedJwtBuilder` at `tests/ShopFlow.Auth.IntegrationTests/Authorization/NarrowedJwtBuilder.cs`** (Sprint-10.5 U4) shipped MSBuild `<Compile Include>`-linked into Inventory + Inbound + Outbound IntegrationTests csprojs. Sprint-11 U3 reuses it from Outbound.IntegrationTests.

7. **Sprint-3-redux saga state via `outbound_saga_transitions`** table (Sprint-7 U1 introduced; queried for SagaPipeline rendering). U3 verifies state advance by polling-with-timeout DbContext read against this table after pick-confirm HTTP call (alternative: MT TestHarness async-completion is canonical but fixture-heavier per Sprint-3-redux U9 carry — poll-with-timeout is the pragmatic Sprint-11 choice per KTD5).

8. **Sprint-10.5 SEC-001 carry**: legacy tenants brought up via raw `shopflow-migrate apply` (not `provision`) lack `role_permissions` rows entirely. Sprint-11's deploy step inherits the operator-runbook requirement — run `shopflow-migrate seed-owner --tenant=<slug>` for every pre-Sprint-9 tenant before deploying Sprint-11 (so the Picker row also gets seeded via the re-run path).

---

## Requirements Traceability

Origin R-IDs traced into plan units:

| Origin | Plan touch points |
|---|---|
| R1 (Picker role with 4-key perm[] baseline) | U1 |
| R2 (`RolePermissionsSeed` extension; idempotent ON CONFLICT DO NOTHING) | U1 |
| R3 (Owner creates Picker via /admin/users; no new code paths) | U3 verification (E2E test exercises the path) |
| R4 (Picker login via standard auth flow; JWT carries 4 keys) | U3 verification |
| R5 (Picker self-service password change via /profile/security; no force-change-on-first-login) | Scope boundary; no plan unit (Sprint-9.5 U6 + Sprint-9 already shipped) |
| R6 (Sidebar perm-filter shows Orders + Inventory; hides Inbound + Admin under Picker JWT) | KTD7 — Sprint-9.5 U8 baseline test inherited; no new Sprint-11 test (per brainstorm deferred Q5) |
| R7 (Picker sees /orders dashboard read-only; write controls hidden) | KTD7 — Sprint-10.5 U5 baseline; no new Sprint-11 test |
| R8 (ConfirmPick + MarkPickFailed buttons render visible on AwaitingPick orders) | U2 (plan-time decision: add buttons in Sprint-11; brainstorm explicitly deferred to planning per origin Outstanding Questions) |
| R9 (Picker sees /inventory dashboard read-only; write controls hidden) | KTD7 — Sprint-10.5 U5 baseline |
| R10 (Picker JWT → ConfirmPick → 200 + saga advance to AwaitingPack) | U2 (button + hook) + U3 (E2E verification) |
| R11 (Picker JWT → MarkPickFailed → 200 + saga compensation) | U2 (button + hook) + U3 (optional secondary E2E test if scope permits) |
| R12 (Picker JWT → other 31 endpoints → 403; inherited from Sprint-10.5 U4) | No new test — Sprint-10.5 U4 already proves this |
| R13 (Picker JWT → /hub/negotiate → success; SignalR push delivers) | U3 verification (negotiation passes; push delivery verified by Sprint-7 useSignalR coverage) |
| R14 (Audit observation: `outbound_saga_transitions` records the saga state advance) | U3 verification (auth_audit_log assertion DROPPED per F3 — no Sprint-9 handler instrumentation exists; storage layer only) |
| R15 (E2E happy-path Docker integration test at Outbound.IntegrationTests/Picker/PickerHappyPathTests.cs) | U3 |
| R16 (Build clean across 47 projects; Sprint-10.5 baselines preserved) | All units' verification gates |
| AE1 (Picker row in role_permissions with 4 keys) | U1 |
| AE2 (Picker login → JWT decodes to 4 keys) | U3 |
| AE3 (Sidebar under Picker JWT shows Orders + Inventory only) | U2 manual smoke + Sprint-9.5 U8 baseline |
| AE4 (Picker on AwaitingPick order sees ConfirmPick + MarkPickFailed; other write controls hidden) | U2 |
| AE5 (Picker ConfirmPick → 200 + saga advances to AwaitingPack) | U2 + U3 |
| AE6 (Picker → POST /api/v1/inventory/adjustments → 403) | Sprint-10.5 U4 inherited |
| AE7 (Sprint-11 E2E test passes in CI Docker-backed suite) | U3 |

---

## Implementation Units

### U0. Branch cut + opening commit with plan + KTDs

**Goal:** Cut `feat/sprint-11-first-multi-role-surface` from `v0.14.1-sprint-10.5`. Opening commit carries the brainstorm + this plan + 8 KTDs in the commit body. Standard sprint U0 cadence matching Sprint-7 / 8 / 8.5 / 9 / 9.5 / 10 / 10.5.

**Requirements:** None — process unit.

**Dependencies:** None — first unit.

**Files:**

- `docs/brainstorms/2026-05-22-sprint-11-first-multi-role-surface-requirements.md` (already on disk; staged)
- `docs/plans/2026-05-22-003-feat-sprint-11-first-multi-role-surface-plan.md` (this file; staged)

**Approach:**

- `git checkout -b feat/sprint-11-first-multi-role-surface v0.14.1-sprint-10.5`.
- Stage the brainstorm + this plan.
- Commit subject: `feat(sprint-11 U0): branch cut + brainstorm + plan + 8 KTDs`.
- Body lists KTD1-KTD8 (one-line summary each).

**Patterns to follow:**

- Sprint-10.5 U0 (`f7422d4`).
- Sprint-10 U0 (`742854e`).

**Test scenarios:** None — branch cut.

`Test expectation: none -- branch cut + docs only; no code change.`

**Verification:**

- Branch `feat/sprint-11-first-multi-role-surface` exists locally.
- `git log --oneline -1` shows the U0 commit with KTDs in body.
- `git status` clean except for any pre-existing `.claude/settings.json` / `node_modules/` drift.

---

### U1. `RolePermissionsSeed` extension — Picker baseline (4 keys) + idempotency

**Goal:** Extend `RolePermissionsSeed` (Sprint-9 U12 — at `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs`) to insert a Picker row in `role_permissions` with the canonical 4-key baseline (`outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`) at every tenant provision. Idempotent via `ON CONFLICT DO NOTHING` on the unique constraint — re-running the seed against an existing tenant does NOT overwrite Owner-customized Picker keys. Update + extend the existing seed tests to cover the new row.

**Requirements:** R1, R2. Covers AE1.

**Dependencies:** U0.

**Files:**

- `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` (modify — add Picker baseline alongside the existing Owner insert; ALSO update the class-level XML doc comment which currently says "Picker + Dispatcher start empty" — that's no longer true for Picker)
- `tests/ShopFlow.Migrate.UnitTests/Provisioning/RolePermissionsSeedTests.cs` (modify — add Picker baseline assertion to the existing Owner test class)
- `tests/ShopFlow.Migrate.IntegrationTests/Provisioning/RolePermissionsSeedIntegrationTests.cs` (modify or extend — assert that after `SeedAsync()` against a fresh tenant DB, the `role_permissions` table contains exactly 24 + 4 = 28 rows (Owner key rows + Picker key rows; ONE ROW PER `(role, permission_key)` composite PK per the real schema); idempotency-additive scenarios per the corrected contract below)
- `web/src/lib/auth/pickerBaseline.ts` (new — exports `PICKER_BASELINE_PERMS: readonly string[]` carrying the same 4 strings, consumed by both Sprint-9.5 U8 Sidebar test fixture + future client-side reference. Prevents Sprint-11 baseline drift vs the Sidebar synthetic-JWT test per doc-review adv-007)

**Approach:**

- Read the existing `RolePermissionsSeed.SeedAsync` body. The Owner insert today inserts ONE ROW PER permission key (composite PK `(role, permission_key)` confirmed by `RolePermissionConfiguration.cs` lines 21-23 — no `tenant_id` column; per-tenant boundary is the DB itself per ADR-0003). The same pattern extends to Picker: a hardcoded 4-string array iterated as 4 separate INSERT rows.
- Picker constants source: reference `PermissionKeys.X` constants directly (NOT string literals). Sprint-10 KTD2 pattern — catalog rename would surface as compile error in `RolePermissionsSeed`.
- **Idempotency contract is ADDITIVE-ONLY (KTD1 corrected wording)**: `INSERT ... ON CONFLICT DO NOTHING` on the composite PK means re-seeding RE-INSERTS any baseline rows that were deleted (the missing-row gets re-inserted). Owner ADDITIONS beyond baseline survive across re-seed (ON CONFLICT skips existing rows). Owner DELETIONS of baseline keys do NOT survive — they revert on next `provision`. This is the surprising semantic — document it in U6 Auth AGENTS.md update + sign-off explicitly so operators are aware.
- The seed runs on every `shopflow-migrate provision`; it does NOT run on `shopflow-migrate apply` (Sprint-10.5 SEC-001 carry — operator-runbook requirement for legacy tenants).
- Update the existing class-level XML doc comment that says "Picker + Dispatcher start empty (the Owner admin editor populates them via the U9 surface)" — Picker no longer starts empty post-Sprint-11; Dispatcher still does (Sprint-12).

**Patterns to follow:**

- `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` existing Owner-insert pattern (Sprint-9 U12).
- `tests/ShopFlow.Migrate.UnitTests/` existing `RolePermissionsSeedTests` class shape.
- `tests/ShopFlow.Migrate.IntegrationTests/` existing Sprint-8 U10 / Sprint-8.5 U11 `RolePermissionsSeedIntegrationTests` shape — real Postgres via Testcontainers.

**Test scenarios:**

- **Covers AE1.** Happy path (unit): given a fresh in-memory DbContext-equivalent, when `RolePermissionsSeed.SeedAsync()` runs, 4 separate rows are inserted with `role='Picker'` + `permission_key` ∈ {`outbound.orders.read`, `outbound.orders.pick-confirm`, `inventory.read`, `hub.connect`}. No additional Picker rows; no missing Picker rows.
- Happy path (integration): real Postgres via Testcontainers; after `SeedAsync()`, query `role_permissions` table; assert 28 rows (24 Owner key-rows + 4 Picker key-rows); assert the 4 Picker rows match the canonical baseline.
- Idempotency-no-duplicates (integration): run `SeedAsync()` twice against the same tenant DB; assert `role_permissions` table still has exactly 28 rows; no duplicated `(role, permission_key)` rows (composite PK + ON CONFLICT DO NOTHING).
- **Idempotency-additive contract (integration)** — replaces the prior incorrect "preserves-customization" scenario. Three sub-scenarios:
  - **Additive preservation**: seed, then manually INSERT an extra row `('Picker', 'inbound.pos.read')` simulating Owner-granted-extra-via-RolePermissionsEditor, then re-run `SeedAsync()`; assert Picker now has 5 rows (4 baseline + 1 Owner-added; ON CONFLICT skipped existing baseline rows; Owner addition survived).
  - **Deletion reversion (KTD1 semantic)**: seed, then manually DELETE the `('Picker', 'inventory.read')` row simulating Owner-removed-via-RolePermissionsEditor, then re-run `SeedAsync()`; assert Picker has 4 rows again (ON CONFLICT path RE-INSERTED the missing baseline row). This is the surprising contract; the test pins it explicitly.
  - **No-mutation idempotency**: seed twice in succession with no manual edits; assert exactly 28 rows + no row's `created_at` was overwritten.
- Catalog integrity: each of the 4 Picker baseline keys is in `PermissionKeys.All` (compile-time guaranteed by KTD2 — direct constant reference).
- Doc-comment update: assert the file-level XML doc comment no longer claims "Picker + Dispatcher start empty" (manual code-review check; not an automated assertion).

**Verification:**

- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings.
- `dotnet test tests/ShopFlow.Migrate.UnitTests/` → all passing including the new Picker baseline unit test. Sprint-10.5 baseline preserved.
- `dotnet test tests/ShopFlow.Migrate.IntegrationTests/` (Docker-backed) → all passing including the 3 new integration scenarios. Skip-marked locally per Sprint-1+ posture; CI runs full Docker suite.
- Sprint-9 / Sprint-10 / Sprint-10.5 Migrate.* baselines preserved.

---

### U2. ConfirmPick + MarkPickFailed UI buttons + `useOrderMutations` hook extensions

**Goal:** Add ConfirmPick + MarkPickFailed action buttons to the order detail route (`web/src/routes/_auth/orders/$orderId.tsx`) with `usePerm` (reactive — Sprint-10.5 KTD3) gates + button-click → `useOrderMutations` extension hooks (`confirmPick` + `markPickFailed`) that POST to the Sprint-10 / Sprint-3-redux endpoints with Idempotency-Key + toast feedback + TanStack Query invalidation. Sprint-7 `useOrderMutations.seedOrder` pattern carries forward.

**Requirements:** R8, R10, R11. Covers AE4, AE5.

**Dependencies:** U0. **No dependency on U1** — frontend-only; backend policies already exist (Sprint-10 KTD8 maps both actions to `OutboundOrdersPickConfirm`).

**Files:**

- `web/src/routes/_auth/orders/$orderId.tsx` (modify — add 2 buttons in a button bar inserted **immediately below the SagaPipeline section, above OrderLineItems** per DL-002; gated by `usePerm('outbound.orders.pick-confirm')`; conditional rendering on saga state == `AwaitingPick`)
- `web/src/components/orders/MarkPickFailedModal.tsx` (NEW — Sprint-6 KTD9 Modal primitive wrapper with a labeled `<textarea>` for the reason + Confirm/Cancel buttons; reason validated non-empty client-side per F4 lock + DL-004 contract; reuses existing Modal Esc-capture + focus-trap)
- `web/src/components/orders/MarkPickFailedModal.test.tsx` (NEW — Vitest scenarios for the modal: opens on trigger, validates empty reason, submits with reason, cancels without firing)
- `web/src/hooks/useOrderMutations.ts` (modify — add shared `createIdempotentMutation` helper + `confirmPick` + `markPickFailed` mutation hooks per KTD3-revised factor pattern)
- `web/src/hooks/useOrderMutations.test.tsx` (modify — add ~10-12 new tests covering 3 mutations × happy/403/500 paths + edge cases; updated test count per doc-review adv-003)
- `web/src/api/orders.ts` (modify if needed — add `confirmPickOrder(orderId)` + `markPickFailed(orderId, reason)` API wrapper functions if not already present; verify by reading the file first)
- `web/src/routes/_auth/orders/$orderId.test.tsx` (modify or new — add gating test scenarios per below; PLUS one axe-core a11y case asserting OrderDetail with AwaitingPick + pick-confirm perm renders 0 violations per DL-008)

**Approach:**

- Read `web/src/routes/_auth/orders/$orderId.tsx` (Sprint-7 U13) before modifying. The current component-tree order is: header → SagaPipeline section → OrderLineItems section → TransitionsLog section → LedgerDrawer. **Button bar insertion point (DL-002)**: immediately below the SagaPipeline section, above OrderLineItems. This aligns the Picker action with the state visualization they just read; an order-detail view's primary call-to-action sits high in the visual hierarchy. The button bar wraps in `usePerm('outbound.orders.pick-confirm')` — if user lacks the key, the entire bar early-returns null (hidden); if user has the key + saga state is `AwaitingPick`, the 2 buttons render.
- **Canonical button labels (DL-004)**: `"Confirm Pick"` (ConfirmPick) and `"Mark Pick Failed"` (MarkPickFailed). Verb-object form; matches backend action names. No icons in Sprint-11 (Sprint-12 polish pass can add them alongside ConfirmPack + ConfirmShip).
- **isPending state (DL-005)**: button replaces its label with the app's standard loading indicator during `mutation.isPending`; `disabled` + `aria-disabled="true"` + `aria-busy="true"`. Match the Sprint-6 AdjustStockModal submit-button pending pattern.
- **Toast strings via `t()` bilingual helper (DL-006)**: success-ConfirmPick: `t('Xác nhận lấy hàng thành công', 'Pick confirmed')`. Success-MarkPickFailed: `t('Đã báo lỗi lấy hàng', 'Pick failed reported')`. Matches Sprint-7 useOrderMutations existing pattern (e.g., `t('Đã tạo đơn mẫu', 'Order seeded')`).
- ConfirmPick button → calls `confirmPick.mutate(orderId)`. On success: toast + invalidate `['orders', orderId]` query so the saga state refetches and the button disappears (saga has advanced to AwaitingPack). On 403: surface `ApiError.errorCode` in error toast (defense-in-depth — should not happen given the perm gate; tests + accidents catch it).
- MarkPickFailed button → opens `MarkPickFailedModal` (NEW component) with labeled `<textarea>` + Confirm/Cancel. **Modal locked as the only path per F4 — `window.prompt()` escape hatch removed (KTD2-revised wording)**. Reason field validated non-empty client-side; on submit, calls `markPickFailed.mutate({ orderId, reason })` and closes the modal on success. Backend `MarkPickFailedRequest(string? Reason)` allows null but client enforces non-empty for UX clarity (Feasibility F5 resolution).
- **`useOrderMutations` factor pattern (KTD3-revised per adv-003)**: extract a shared helper `createIdempotentMutation<TReq, TRes>(label, fn, invalidateKeys, toastLabels)` that wraps useMutation + ulid()-per-call lastKey ref + toast push + invalidation. The 3 mutations (seedOrder, confirmPick, markPickFailed) consume it. Each retains its per-mutation env-specific branches (e.g., seedOrder's 404 `environment_not_dev` branch). The aggregator hook returns `{ seedOrder, confirmPick, markPickFailed }`. ~3-hook-factor reorganization; preserves Sprint-7 discipline + reduces duplication.
- Verify `web/src/api/orders.ts` for the wrapper functions. If `confirmPickOrder` + `markPickFailed` wrappers don't exist, add them following the Sprint-7 `seedOrder` shape.
- KTD3-reactive `usePerm` — the route component subscribes via the hook so mid-session perm changes re-render the button bar (catches admin-on-another-tab revoke + refresh-token rotation narrowing).
- KTD8 hidden-by-default — button bar hidden when user lacks the perm. **AwaitingPick-but-lacks-perm silent absence accepted as known trade-off (DL-003)** — Picker onboarding may briefly confuse a misconfigured Picker; future sprint can revisit if real onboarding friction surfaces.
- **Post-200-pre-refetch button-persistence guard (DL-007)**: set a local `justConfirmed`/`justMarkedFailed` boolean state immediately after the mutation resolves 200; use it to hide the button bar optimistically while the query refetch is in-flight. Clears on next render after the query updates. Prevents the brief inconsistency where the toast says "Pick confirmed" but the button is still clickable before the saga-state refetch completes.

**Patterns to follow:**

- `web/src/hooks/useOrderMutations.ts` `seedOrder` mutation pattern (Sprint-7 U8).
- `web/src/components/inventory/AdjustStockModal.tsx` `usePerm`-gated button pattern (Sprint-10.5 U5).
- `web/src/components/inventory/CreateSkuModal.tsx` modal + form + mutation integration (Sprint-6 KTD9 + Sprint-10.5 U5).
- Sprint-10.5 U5 hidden-by-default early-return shape; useToast for action feedback.

**Test scenarios:**

- **Covers AE4.** Happy path: render `OrderDetailRoute` with a mock useAuth holding `outbound.orders.pick-confirm` + saga state `AwaitingPick`; assert ConfirmPick + MarkPickFailed buttons exist via `screen.queryByRole('button', { name: /confirm pick/i })`.
- **Covers AE4.** Gating-perm path: render with mock useAuth lacking `outbound.orders.pick-confirm` + saga state `AwaitingPick`; assert both buttons are absent.
- Gating-state path: render with mock useAuth holding the key + saga state `AwaitingPack` (or `Shipped` etc.); assert both buttons are absent (state-gated, not just perm-gated).
- **Covers AE5.** Mutation happy path: click ConfirmPick → POST `/api/outbound/orders/{id}/confirm-pick` is fired with `Idempotency-Key` header; on 200 response, success toast appears + `['orders', orderId]` query is invalidated. Mirrors Sprint-7 `seedOrder` test scenarios.
- Mutation 403 path: click ConfirmPick with mock fetch returning 403 + body `{"code": "..."}`; error toast appears surfacing the errorCode + traceId; saga state unchanged.
- Mutation 500 path: click ConfirmPick with mock fetch returning 500; error toast appears with idempotencyKey + traceId; offers retry.
- MarkPickFailed flow: click MarkPickFailed → dialog opens; submit with reason "test reason" → POST `/api/outbound/orders/{id}/mark-pick-failed` with `reason` body + Idempotency-Key; success path mirrors ConfirmPick.
- ULID-per-call: 2 sequential ConfirmPick calls generate 2 different Idempotency-Key values (audit-only dedupe per Sprint-7 KTD).
- Edge: rapid double-click of ConfirmPick — `mutation.isPending` disables the button during the in-flight call; no double-fire.

**Verification:**

- `npx vitest run web/src/hooks/useOrderMutations.test.tsx` passes.
- `npx vitest run web/src/routes/_auth/orders/$orderId.test.tsx` passes including new gating + state tests.
- Sprint-9.5 / Sprint-10.5 frontend baseline preserved (~480 passing / 4 pre-existing Sprint-7 a11y failures unchanged).
- A11y smoke harness passes for the button-bar variants (visible + hidden states).

---

### U3. E2E happy-path Docker-backed integration test — Picker login + ConfirmPick + saga advance

**Goal:** Add a Docker-backed end-to-end integration test at `tests/ShopFlow.Outbound.IntegrationTests/Picker/PickerHappyPathTests.cs` that pins the full Sprint-11 chain: Owner-creates-Picker → Picker-logs-in-via-/api/auth/login → Picker-calls-POST-/api/outbound/orders/{id}/confirm-pick → 200 + saga advances to `AwaitingPack` + audit log records the action. Skip-marked locally per Sprint-1+ posture; CI runs the full Docker-backed suite.

**Requirements:** R3, R4, R10, R13, R14, R15. Covers AE2, AE5, AE7.

**Dependencies:** U0, U1. Cross-depends on U2 (UI buttons) indirectly — but the E2E test exercises HTTP directly, not the UI; U2's UI work is verified by its own Vitest tests. U3 can ship even if U2 frontend buttons are deferred (the HTTP endpoint is the same).

**Files:**

- `tests/ShopFlow.Outbound.IntegrationTests/Picker/PickerHappyPathTests.cs` (NEW — 1 fact, sealed class, `[Trait("Category", "Integration")]`, `[Fact(Skip = "Sprint-11 U3: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon")]`)
- `tests/ShopFlow.Outbound.IntegrationTests/Picker/PickerFixture.cs` (NEW — net-new HTTP test infrastructure of comparable complexity to `AuthTenantFixture` per doc-review SG-001 + F4. Boots Auth.Api WAF + Outbound.Api WAF against shared Testcontainers Postgres + Redis; applies BOTH module schemas via `IModuleMigrationRegistry` (Auth.Api Program.cs does NOT auto-migrate); seeds Owner via OwnerSeed + RolePermissionsSeed (extended with Picker baseline via U1); exposes `HttpClient` per WAF + `seedPicker(email)` helper that calls `POST /api/auth/admin/users` against the Owner-authenticated test client)

**Approach:**

- **Dual-WAF composition strategy + fallback (doc-review adv-004 + KTD4 corrected)**: U3 implementer first runs a 30-min spike to validate `WebApplicationFactory<Program>` resolves the right Program when both Auth.Api + Outbound.Api csprojs are referenced from Outbound.IntegrationTests. Sprint-10.5 U4 already proved single-WAF composition; the dual-WAF question is whether `Program` symbol disambiguation via `extern alias` works cleanly. **Spike fallback**: if extern-alias treatment costs too much, drop "Picker logs in via /api/auth/login" verification from U3; seed Picker user + JWT directly via NarrowedJwtBuilder (Sprint-10.5 U4 helper). The saga + audit chain remains verifiable. Plan permits BOTH paths; spike outcome at U3 build time picks one.
- **Pre-condition: apply Auth schema to shared tenant DB** (doc-review F4) — `PickerFixture` invokes `IModuleMigrationRegistry` (or hand-calls `AuthDbContext.Database.MigrateAsync` alongside `OutboundDbContext.Database.MigrateAsync`) before either WAF handles its first request. Without this, the Auth tables (users / role_permissions / auth_audit_log) don't exist when Auth.Api tries to read them and login throws at the EF query layer.
- Arrange:
  1. Provision a tenant DB via `IModuleMigrationRegistry.ApplyAllAsync()` (or equivalent) — applies BOTH Auth + Outbound schemas + RolePermissionsSeed (now seeding Owner-24 + Picker-4 per U1) + OwnerSeed.
  2. Boot Auth.Api WAF (if dual-WAF path chosen at spike); log Owner in via `POST /api/auth/login`; capture Owner's JWT. (Spike-fallback path: skip; mint Owner JWT via NarrowedJwtBuilder directly.)
  3. Owner JWT → `POST /api/auth/admin/users` with `email: "picker@<tenant>.test"`, `role: "Picker"`. Capture temp password from the response body (verify Sprint-9.5 U7 response shape includes it; if not, fallback to direct DbContext user-insert + NarrowedJwtBuilder).
  4. **Seed order in AwaitingPick state via direct DbContext (F1 saga-seeding resolution)**: bypass the saga's natural OrderPlaced → ReserveStockV1 → StockReservedV1 flow (which requires Inventory.Api consumer that the InMemory MT transport doesn't run). Direct DbContext writes: (a) INSERT into `orders` with `Status = AwaitingPick`; (b) INSERT into the MT-managed `FulfillmentSagaState` row with `CurrentState = "AwaitingPick"` + matching `CorrelationId`. Document the FulfillmentSagaState table/column shape as U3 implementer-time research (read Sprint-3-redux MT saga storage config).
  5. Log Picker in via `POST /api/auth/login` with temp password (dual-WAF path) OR mint Picker JWT via NarrowedJwtBuilder (spike-fallback). Capture JWT.
  6. Decode Picker's JWT payload (no signature verify — JwtBearer downstream catches that). Assert: `role == "Picker"`, `perm[]` contains exactly the 4 baseline keys.
- Act:
  - **Warmup**: `GET /api/outbound/orders/{id}` to flush EF/MT lazy init per KTD5-revised (10s baseline timeout assumes warmed up state).
  - Picker JWT → `POST /api/outbound/orders/{id}/confirm-pick` with the order ID from step 4.
- Assert (synchronous):
  - HTTP 200 response.
  - Picker JWT round-trip: JWT shape verified (4 keys in `perm[]`).
- Assert (poll-with-timeout per KTD5-revised):
  - Within **10 seconds** (baked-in baseline, not reactive 5s→10s bump), the saga state in `outbound_saga_transitions` advances to `AwaitingPack` (or transitions through it). Poll every 200ms; fail with a clear "expected AwaitingPack within 10s, observed: <current state>" message on timeout.
  - **Saga transition row written** with `from_state = "AwaitingPick"` + `to_state = "AwaitingPack"` (Sprint-7 instrumentation).
  - **(Removed per F3)**: `auth_audit_log` row assertion DROPPED. No Sprint-9 command handler calls `IAuthAuditLogRepository.AppendAsync` today; the storage layer ships without handler instrumentation. U6 sign-off + Auth AGENTS.md document the gap as Sprint-11.5/12 hardening.
- Cleanup: container teardown via IAsyncLifetime (Sprint-10.5 U4 pattern carries).

**Patterns to follow:**

- `tests/ShopFlow.Outbound.IntegrationTests/Authorization/OutboundAuthorizationFixture.cs` (Sprint-10.5 U4 — WAF + Testcontainers Postgres + DevSecret override + JWT factory pattern).
- `tests/ShopFlow.Auth.IntegrationTests/AuthTenantFixture.cs` (Sprint-9 + Sprint-8 — tenant provisioning + OwnerSeed + RolePermissionsSeed).
- `tests/ShopFlow.Auth.IntegrationTests/AuthCrossTenantTests.cs` (Sprint-9.5 U9 — multi-host WAF composition pattern).
- `tests/ShopFlow.Outbound.IntegrationTests/Sagas/SagaHappyPathTests.cs` (Sprint-3-redux U9 — saga state assertion pattern; poll-with-timeout reference if the existing tests use it).
- Sprint-9 KTD1 perm-claim shape — one `Claim("perm", value)` per key for JsonWebTokenHandler array-flattening.

**Test scenarios:**

- **Covers AE7 (revised).** Sprint-11 happy-path (1 fact): the chain Owner-creates-Picker → Picker-logs-in (or NarrowedJwtBuilder-fallback) → ConfirmPick-200 → saga-state-advance-to-AwaitingPack-within-10s passes end-to-end against real Postgres + Redis. **Audit-log row assertion removed** per F3 (Sprint-9 storage-only state).
- Edge case (NOT a separate fact; embedded): Picker's `perm[]` claim contains exactly 4 entries (matches `PICKER_BASELINE_PERMS` constant from `web/src/lib/auth/pickerBaseline.ts`).
- Edge case: the order's `Status = AwaitingPick` + saga `CurrentState = "AwaitingPick"` BEFORE the ConfirmPick call (precondition verification — confirms the direct DbContext seed shape worked).
- Edge case: if the saga doesn't advance within 10 seconds, the test fails with a message naming the actual `CurrentState` (diagnoses stuck saga vs warmup miss).
- Negative case (NOT a separate fact in Sprint-11 — Sprint-10.5 U4 33+1 already proves rejection): Sprint-10.5 U4's existing 403 tests prove Picker rejection for the 31 other actions. Sprint-11 doesn't duplicate that coverage.

`Test expectation: 1 [Fact(Skip)] in Sprint-11 (CI runs Docker suite); ~7 internal assertions cover the chain.`

**Verification:**

- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings.
- `dotnet test tests/ShopFlow.Outbound.IntegrationTests/` locally → the new test Skips per Sprint-1+ posture (no Docker daemon). Sprint-10.5 baseline preserved.
- CI nightly + per-PR Docker-backed job → the new test passes against real Postgres + Redis + the Sprint-11-shipped Picker seed.
- Manual smoke (if Docker available on the developer machine at any point during Sprint-11): un-Skip the test temporarily, run, verify the chain works, re-Skip before commit. Documented in U4 sign-off as an optional verification step.

---

### U4. Sign-off + Auth AGENTS.md + README + CLAUDE.md + CHANGELOG + tag

**Goal:** Capture the Sprint-11 sign-off, update Auth per-module AGENTS.md (Picker baseline + provisioning flow note), update README + CLAUDE.md current-stage block, append CHANGELOG entry, create annotated tag `v0.15.0-sprint-11` (minor bump per KTD8). Standard sprint sign-off cadence matching Sprint-7 / 8 / 8.5 / 9 / 9.5 / 10 / 10.5.

**Requirements:** None directly — documentation + release marker.

**Dependencies:** U1, U2, U3.

**Files:**

- `docs/phase-gates/2026-05-22-sprint-11-signoff.md` (new)
- `src/Services/Auth/AGENTS.md` (light update; ≤ 50 lines per root AGENTS.md §82)
- `README.md` (current-stage block + shield badge)
- `CLAUDE.md` (current-stage block + Sprint-10.5 demoted to history per the established history-block pattern)
- `docs/CHANGELOG.md` (Sprint-11 entry matching Sprint-10.5 entry shape)

**Approach:**

- Sign-off doc mirrors [Sprint-10.5 sign-off](../phase-gates/2026-05-22-sprint-10.5-signoff.md) shape (~7 sections — units / KTDs / trade-offs / verification gates / next step).
- Auth `AGENTS.md` adds one line under "Hard rules" noting "Sprint-11 Picker baseline pre-seeded via `RolePermissionsSeed` extension (4 keys: outbound.orders.read + outbound.orders.pick-confirm + inventory.read + hub.connect); idempotent ON CONFLICT DO NOTHING preserves Owner customizations; Picker provisioned via `/admin/users` canonical path; Picker MFA not enforced (Sprint-12+ decision); force-change-on-first-login not enforced (future production hardening)."
- README current-stage block flipped from Sprint-10.5 to Sprint-11; shield badge updated; Sprint-10.5 block demoted to historical.
- CLAUDE.md current-stage block flipped to Sprint-11 with full units list + 8 KTDs + trade-offs carried forward to Sprint-12+; Sprint-10.5 demoted with "Sprint-10.5 history (kept for context; tag v0.14.1-sprint-10.5)" header.
- CHANGELOG entry matches Sprint-10.5's shape.
- Tag `v0.15.0-sprint-11` annotated (KTD8 — minor bump matching Sprint-9.5 precedent for net-new feature work; Sprint-11 introduces a new role + UI surfaces).
- After tagging, push branch + tag to origin per the standing user preference (memory feedback `push-before-phase-switch`).

**Patterns to follow:**

- Sprint-10.5 U6 sign-off commit (`6ed85a9`) — same shape, same section ordering.
- Sprint-10 U5 sign-off commit (`12dff68`).

**Test scenarios:** None — pure documentation + git operations.

`Test expectation: none -- documentation and tag commit; no behavioral change.`

**Verification:**

- Sign-off doc exists at the named path and references each of U0-U4 commit SHAs.
- Auth `AGENTS.md` ≤ 50 lines.
- `CLAUDE.md` current-stage block reflects Sprint-11 completion.
- Tag `v0.15.0-sprint-11` exists at HEAD: `git rev-parse v0.15.0-sprint-11` resolves; `git show v0.15.0-sprint-11` shows the U4 sign-off commit.
- Push to origin succeeds (branch + tag).

---

## Scope Boundaries

Carried verbatim from origin plus plan-time additions.

### In-sprint scope additions surfaced during planning

- **ConfirmPick + MarkPickFailed UI buttons** (U2) — brainstorm R8 deferred-to-implementation question resolved at plan time: the buttons land in Sprint-11 (per the user's plan-time decision). Fulfills the Sprint-10.5 KTD7 deferred carry; ConfirmPack + ConfirmShip with Dispatcher follows in Sprint-12.
- **`useOrderMutations` hook extensions** (U2) — `confirmPick` + `markPickFailed` mutation hooks added to the Sprint-7 hook, following the existing `seedOrder` Idempotency-Key + toast + invalidation pattern. Required precondition for the UI buttons.

### Carried from origin (unchanged)

- Single Picker role (not multi-role hand-off; Dispatcher → Sprint-12 candidate).
- Gated existing UI (not dedicated picker queue).
- E2E Docker-backed happy-path test (no separate Sprint-10.5-style positive-companion 200-suite; the E2E exercises the positive path comprehensively).
- Provisioning via Sprint-9.5 U7 `/admin/users` (no CLI extension).
- Picker MFA not enforced (Sprint-12+ decision).
- Force-change-on-first-login deferred (future production hardening).

### Deferred to Follow-Up Work

- **Sprint-11.5 — force-change-on-first-login via existing PasswordResetToken** (per SEC-001 mitigation): `/admin/users` CreateUser issues a single-use reset token instead of a plain temp password; first-login routes through `/reset-password` and forces user to set their own password. Reuses Sprint-9 PasswordResetToken infrastructure; no new UI sprint needed.
- **Sprint-11.5/12 — `IAuthAuditLogRepository` handler instrumentation** (per F3 + SEC-002): wire `LoginCommandHandler` + key Auth handlers (refresh, MFA, admin actions) + `OrdersController.ConfirmPickAsync` to `AppendAsync`. Closes the storage-layer-only state Sprint-9 left.
- **Sprint-12 — Dispatcher role + multi-role workflow hand-off**. Dispatcher with `outbound.orders.pack-confirm` + `outbound.orders.ship-confirm` keys. ConfirmPack + ConfirmShip UI buttons on `$orderId.tsx` (alongside Sprint-11's ConfirmPick + MarkPickFailed). Owner-Picker-Dispatcher workflow E2E test.
- **Sprint-12+ — per-role minimum-keys floor** (per SEC-003): extend `RolePermissionsCommandHandler` KTD13 guard with per-role minimum-keys validation (analogous to `OwnerCritical`) so Owner cannot accidentally strip Picker/Dispatcher to zero keys.
- **Dedicated picker queue UI** (`/picker` route, filter / sort / batch confirm) — if Sprint-11's gated existing UI proves insufficient.
- **Picker MFA required-enrollment** — per-role MFA mandate decision; Sprint-12+ alongside Dispatcher.
- **ProfileSecurityScreen `useMe()` migration** (Sprint-9.6 carry).
- **`shopflow-migrate seed-user --role=Picker` CLI extension** — future ops sprint if needed.
- **Extended Picker (+ `inbound.pos.read`)** — when Inbound frontend ships.
- **`RolePermissionsSeed` widening to other roles' baselines** — Dispatcher / Receiving / Ops baselines land alongside each role's introduction.
- **`outbound.orders.cancel` orphan-key attachment surface** — unchanged Sprint-10.5 carry.
- **EditSkuModal.test.tsx Vitest worker crash investigation** — Sprint-10.6 carry; unrelated to Sprint-11.
- **Phase-3 observability dashboards** — per-permission denial rates per tenant + auth_audit_log partitioning + KMS/Vault TOTP KEK; unchanged Sprint-10.5 carry.
- **Server-side forced-disconnect on role-permission update (SEC-002 hardening)** — closes the 15-min hub.connect revocation lag; unchanged Sprint-10.5 carry.

---

## Key Technical Decisions

- **KTD1 — `RolePermissionsSeed` Picker baseline ships ADDITIVE-ONLY semantics via `ON CONFLICT DO NOTHING`** on the actual composite PK `(role, permission_key)` (verified — `RolePermissionConfiguration.cs` lines 21-23; per-tenant boundary is the entire DB per ADR-0003, no `tenant_id` column). The 4-key Picker baseline is inserted as 4 separate rows (one per key), reference-by-constant (`PermissionKeys.OutboundOrdersRead` etc., NOT string-literal — Sprint-10 KTD2 pattern). **Idempotency contract (corrected per doc-review)**: re-running seed restores any baseline keys that were deleted (the missing rows get re-inserted); Owner ADDITIONS beyond baseline ARE preserved across re-seed (ON CONFLICT skips existing rows). The deletion-reversion is the surprising semantic — operator who removes `inventory.read` from Picker via `/admin/role-permissions` will see it return after the next `shopflow-migrate provision`. Document this contract in U6 Auth AGENTS.md + sign-off. Initial plan-time wording was inverted; reality is the additive-only shape.
- **KTD2 — ConfirmPick + MarkPickFailed UI buttons land in Sprint-11** (plan-time decision; brainstorm R8 was explicitly deferred to plan-time, plan resolved it). Fulfills the Sprint-10.5 KTD7 deferred carry. Future ConfirmPack + ConfirmShip ship in Sprint-12 alongside Dispatcher role provisioning + multi-role workflow hand-off. Bundling ConfirmPick + MarkPickFailed together is near-free since both share the same backend perm key (Sprint-10 KTD8 maps both to `OutboundOrdersPickConfirm`). **MarkPickFailed reason capture locked to Sprint-6 KTD9 Modal primitive** — `window.prompt()` escape hatch removed (jsdom doesn't implement it; UX inconsistency vs ConfirmPick's toast pattern; backend `MarkPickFailedRequest(string? Reason)` allows null but client validates non-empty for UX clarity).
- **KTD3 — `useOrderMutations` (Sprint-7) gets a thin shared helper `createIdempotentMutation<TReq, TRes>(label, fn, invalidateKeys)`** that the existing `seedOrder` + new `confirmPick` + `markPickFailed` mutations consume. The aggregator hook returns `{ seedOrder, confirmPick, markPickFailed }`. Idempotency-Key (ULID-per-call, audit-only dedupe per Sprint-7 KTD), toast feedback (via `useToast` + `t()` bilingual helper per Sprint-9.5 wire), TanStack Query invalidation on success. Each mutation owns its `useRef` for last-key + per-mutation env-error branches (e.g., 404 `environment_not_dev` for `seedOrder`). **Test count corrected (doc-review adv-003)**: Success Criteria's "~4 useOrderMutations tests" was off; realistic count is ~10-12 (3 mutations × happy + 403 + 500 paths × variations); update Success Criteria accordingly.
- **KTD4 — E2E test placement LOCKED: `tests/ShopFlow.Outbound.IntegrationTests/Picker/PickerHappyPathTests.cs`** (no longer "deferred to implementer time"; alternative placements ruled out at plan time). Rationale: the dominant surface under test is the Outbound saga + pick-confirm endpoint; Outbound.IntegrationTests already has WAF infrastructure from Sprint-10.5 U4. **Dual-WAF caveat (doc-review adv-004 + Feasibility F4)**: U3 must compose Auth.Api WAF + Outbound.Api WAF + apply Auth schema migration to the shared tenant DB (Auth.Api's Program.cs doesn't auto-migrate). Realistic fallback plan if dual-WAF `Program` symbol disambiguation costs too much (CS0433 risk per Sprint-10.5 precedent): drop "Picker logs in via /api/auth/login" verification from U3; seed Picker DB row directly + generate Picker JWT via `NarrowedJwtBuilder` (bypassing real login); keep saga + audit chain verifiable. U3 Approach documents BOTH paths; implementer picks at U3 build time based on a 30-min spike outcome.
- **KTD5 — Saga state assertion: poll-with-timeout DbContext read** against `outbound_saga_transitions`. Poll every 200ms for up to **10 seconds** (baked-in baseline, not reactive 5s → 10s bump per doc-review F6 + adv-005). 10s accounts for Testcontainers cold-start + Argon2id login latency (Sprint-8 KTD9 OWASP 2026 profile, 200-500ms per call) + MT in-process delivery + saga consume. Add a pre-poll warmup HTTP call (`GET /api/outbound/orders/{id}` immediately after the confirm-pick POST) to flush EF/MT lazy init. Sprint-3-redux MT TestHarness is the canonical fallback if 10s poll flakes — documented in Risk Analysis.
- **KTD6 — Picker test-user email convention: `picker@<tenant>.test`** for fixture reproducibility. Deterministic; matches `<tenant>.test` TLD reserved for testing per RFC 6761. Useful across multiple test classes if Sprint-12+ adds Dispatcher with `dispatcher@<tenant>.test` analogously.
- **KTD7 — Sidebar-under-Picker-JWT verification deferred to Sprint-9.5 U8 baseline**. Sprint-9.5 U8 already proves the `permRequired` filter pattern via synthetic narrowed JWTs (Sidebar test class includes Owner / Picker / no-session visibility cases per AE4). A Sprint-11-specific test against a real Picker JWT would be redundant. Brainstorm deferred Q5 resolved: no new test in U2.
- **KTD8 — Version bump `v0.15.0-sprint-11`** (minor bump) matching Sprint-9.5 precedent. Sprint-2.5 / 4.5 / 7.5 / 8.5 / 10.5 used patch bumps for trade-off closures + point releases. Sprint-9.5 used minor because it shipped the Notification module (net-new). Sprint-11 introduces a new role + UI buttons + saga workflow surface — net-new feature work, not closure work. Minor bump fits.

---

## Success Criteria

Carried from origin:

- A non-Owner role works end-to-end under the defense-in-depth stack — login + UI navigation + action authorization all pass for actions in Picker's perm[] and fail with 403 for actions outside it.
- Sprint-10.5 U5's `usePerm` (reactive) gates demonstrably hide/show the right controls under a real Picker JWT.
- Sprint-10.5 U4's 33 + 1 Docker 403 tests are inherited as Picker-rejection coverage; Sprint-11 adds the E2E positive-path verification for ConfirmPick.
- Sprint-9.5 U7 `/admin/users` + `RolePermissionsEditor` admin surfaces are proven load-bearing — they ARE the canonical provisioning + role-config flow.
- `RolePermissionsSeed` extension ships idempotent + tenant-provision-safe; subsequent runs against an existing tenant don't overwrite Owner-edits to Picker keys.
- Sprint-12 unblocked: Dispatcher role + multi-role workflow hand-off can land on top of Sprint-11's foundation with minimal new architecture.
- `outbound_saga_transitions` (Sprint-7 instrumentation) records the saga's `AwaitingPick → AwaitingPack` advance. **`auth_audit_log` row capture is NOT in scope** (Sprint-9 storage-layer-only state per F3); wiring `LoginCommandHandler` + key Auth handlers to `IAuthAuditLogRepository.AppendAsync` is Sprint-11.5/12 hardening.

Plan-specific additions:

- ConfirmPick + MarkPickFailed UI buttons render correctly under Picker JWT + AwaitingPick state; hide under all other state-perm combinations.
- `useOrderMutations` hook gains 2 new mutations following Sprint-7 discipline (no ad-hoc inline fetch in the route file).
- `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings across 47 projects post-Sprint-11.
- Sprint-9.5 + Sprint-10 + Sprint-10.5 unit-test baselines preserved; Sprint-11 adds ~25-30 new tests (KTD3-revised count): 4 RolePermissionsSeed unit + 4 Migrate.IntegrationTests scenarios (3 baseline + 3 additive contract sub-scenarios) + ~10-12 useOrderMutations + ~5 $orderId route gating + 4 MarkPickFailedModal + 1 a11y axe case + 1 E2E Docker fact.

---

## System-Wide Impact

Sprint-11 introduces a new role-class actor without changing any system architecture. Operationally:

- **No new data model.** No new tables, columns, migrations, or contracts. `role_permissions` table (Sprint-9 U3) gains a new row per tenant via the existing seed pipeline.
- **No new permission keys.** `PermissionKeys.All` stays at 24.
- **Frontend gains `ConfirmPick` + `MarkPickFailed` UI surfaces.** Visible to users holding `outbound.orders.pick-confirm` on orders in `AwaitingPick` state. Sprint-10.5 U5 gating pattern (`usePerm` reactive) carries.
- **`useOrderMutations` API surface expands.** External shape: 2 new mutation hooks exported. Consumers other than `$orderId.tsx` are not affected.
- **Authentication posture per tenant changes after Sprint-11 deploy**: Owner remains the only role with 24 keys; Picker role now exists as a provisionable role with 4 keys. Owner can create Picker users + edit Picker keys via `/admin/role-permissions` (Sprint-9.5 U7).
- **No deployment changes.** Same containers, env vars, secrets, migration entrypoints. SEC-001 carry: legacy tenants need `shopflow-migrate seed-owner` re-run before Sprint-11 deploy so Picker baseline gets seeded.

---

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **F1 idempotency-additive-only contract surprises operator** (KTD1-corrected). Owner removes a Picker baseline key via `/admin/role-permissions`; next `shopflow-migrate provision` re-inserts the removed row. Owner-removed-baseline-key reverts on re-seed. | Medium | Medium | U6 sign-off + Auth AGENTS.md explicitly document the additive-only contract: Owner ADDITIONS survive, Owner DELETIONS of baseline keys revert on re-provision. U1 integration test "deletion-reversion" scenario pins this behavior as a contract test. Operator runbook for Sprint-12 documents the reversion semantic before Dispatcher baseline lands. Future hardening: switch RolePermissionsSeed to a check-then-INSERT-missing-only shape that preserves deletions (out of Sprint-11 scope). |
| **SEC-001 temp password lifetime** — Picker logs in with Owner-issued temp password; no force-change-on-first-login. Owner shares temp via out-of-band channel (Slack / email); Picker can operate indefinitely on it; chat history retains credential. | Medium-High | Medium | **Sprint-11.5 target**: implement force-change-on-first-login via the existing Sprint-9 PasswordResetToken infrastructure — `/admin/users` CreateUser issues a single-use reset token instead of a plain temp password; first-login flow routes through `/reset-password` and forces the user to set their own password. Closes the gap without a new UI sprint. Sprint-11 ships with the temp-password trade-off documented in U6 sign-off + flagged for Sprint-11.5. |
| **SEC-002 actor attribution gap on ConfirmPick** — `outbound_saga_transitions` row written but lacks `actor_id` / `user_id` column. `OrdersController.ConfirmPickAsync` does not read `User.Identity` to attach actor identity to the transition. Multi-Picker scenarios (Sprint-12+) have no non-repudiation record. | Low (Sprint-11; only 1 Picker per tenant) | Medium | Acknowledged trade-off for Sprint-11. **Sprint-11.5/12 hardening**: ConfirmPickAsync reads `User.FindFirstValue(ClaimTypes.NameIdentifier)` and either (a) passes actor_id as event payload metadata for the saga to persist alongside the transition, OR (b) writes an `auth_audit_log` row via `IAuthAuditLogRepository.AppendAsync` with `EventType="outbound.pick.confirmed"` + actor sub claim. AuthAdminController already demonstrates pattern (b). |
| ConfirmPick + MarkPickFailed scope creep — implementer over-expands the reason-capture modal into a full audit-comment-capture flow. | Low | Medium | U2 approach LOCKED to Sprint-6 KTD9 Modal with a single labeled textarea + Confirm/Cancel (per F4 resolution). `window.prompt()` escape hatch REMOVED. Sprint-11.5 / Sprint-12 can refine the modal if real Picker usage surfaces feedback. |
| **F3 audit-log not wired** — `IAuthAuditLogRepository` ships as storage layer only; no Sprint-9 command handler calls `AppendAsync` (verified via grep). LoginCommandHandler does NOT write to `auth_audit_log`. U3's R14 auth_audit_log assertion would fail in CI. | High | Medium | U3 Assert step DROPS the `auth_audit_log` row assertion. R14 + AE7 rewritten to reflect storage-only state. U6 sign-off + Auth AGENTS.md document the gap as Sprint-11.5/12 hardening (wire LoginCommandHandler + key Auth handlers + ConfirmPickAsync to `IAuthAuditLogRepository`). |
| **F4 dual-WAF Program symbol ambiguity** (CS0433) — Outbound.IntegrationTests referencing both Auth.Api + Outbound.Api csprojs hits the same `Program` collision Sprint-10.5 U4 already encountered. | Medium | Medium | KTD4-revised: U3 implementer runs a 30-min spike to validate `extern alias` treatment for the dual-Program disambiguation. **Spike-fallback**: if extern-alias is costly, drop "Picker logs in via /api/auth/login" verification; seed Picker user via direct DbContext + mint Picker JWT via NarrowedJwtBuilder. Plan permits BOTH paths; spike outcome at U3 build time picks one. |
| Saga state advance from `AwaitingPick` to `AwaitingPack` takes longer than expected under Testcontainers cold-start. Test flakes. | Low-Medium | Medium | KTD5-revised: **10s baseline timeout (NOT reactive 5s→10s bump)**. Pre-poll warmup HTTP call flushes EF/MT lazy init. If 10s baseline still flakes in CI: switch to Sprint-3-redux MT TestHarness async-completion pattern (canonical but fixture-heavy). |
| **F4 Auth schema migration on shared tenant DB** — Auth.Api Program.cs does not auto-migrate; OutboundAuthorizationFixture migrates only Outbound schema. Without applying Auth schema, login throws at EF query layer. | Medium | High | `PickerFixture` explicitly invokes `IModuleMigrationRegistry.ApplyAllAsync()` (or hand-calls AuthDbContext.Database.MigrateAsync + OutboundDbContext.Database.MigrateAsync) BEFORE either WAF handles its first request. Sprint-11 U3 Approach step 1 documents this explicitly. |
| Sprint-11 SEC-001 inheritance — legacy tenants that didn't run `seed-owner` before Sprint-10.5 deploy now ALSO can't get Picker rows after Sprint-11 deploy. | Medium | Low | Sprint-11 sign-off + Auth AGENTS.md update reinforce the Sprint-10.5 reseed-required note. The fix is the same: `shopflow-migrate seed-owner --tenant=<slug>` for every pre-Sprint-9 tenant. No new mitigation needed. |
| **SEC-003 no minimum-keys floor for Picker** — Owner can strip Picker of all keys via `/admin/role-permissions`; existing sessions continue for up to 15-min JWT TTL (Sprint-10.5 SEC-002 carry); next login produces zero-key JWT; UI fails closed silently with no error. | Medium | Low-Medium | Acknowledged operator footgun for Sprint-11. Future hardening: extend `RolePermissionsCommandHandler` KTD13 guard with a per-role minimum-keys floor (analogous to `OwnerCritical`) for non-Owner roles. Out of Sprint-11 scope; documented in Deferred. |
| `useOrderMutations.confirmPick` mutation accidentally invalidates `['orders']` broad query when only `['orders', orderId]` is needed — triggers a full list refetch on every pick-confirm. Inefficient but not incorrect. | Low | Low | U2 test scenarios verify `['orders']` broad invalidation is correct behavior (saga state change might affect the list view's saga-state pills). Performance not a concern at portfolio scale. |
| Picker JWT carries unexpected keys (e.g., includes `outbound.orders.pack-confirm` from a Dispatcher-key bleed when Sprint-12 lands). | Very Low | Low | U1's unit test directly asserts Picker's `permission_key` rows are exactly the 4 baseline strings — set equality against the `PICKER_BASELINE_PERMS` constant. Misalignment would fail the test. (Risk row rewritten per adv-006: the original `outbound.orders.write` reference was a nonexistent key.) |

---

## Dependencies / Prerequisites

- **`v0.14.1-sprint-10.5`** as the cut-from tag. Branch + tag pushed to origin.
- **Sprint-9 U12 `RolePermissionsSeed`** at `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` — Sprint-11 U1 extends.
- **Sprint-9 `UserRole` enum recognizes `Picker`** — verified at brainstorm time via Sprint-9.5 U7 admin.ts `EditableRole = 'Picker' | 'Dispatcher'` cross-reference.
- **Sprint-9.5 U7 `/admin/users` + RolePermissionsEditor** — Sprint-11 consumes for Picker user creation.
- **Sprint-9.5 U7 `POST /api/auth/admin/users`** backend endpoint — Sprint-11 U3 calls.
- **Sprint-10 backend per-action policy on `OutboundOrdersPickConfirm`** — Sprint-11 U3 exercises positive path.
- **Sprint-10.5 U1 frontend `admin.ts` catalog** — Picker's 4 keys all exist in the corrected `PERMISSION_KEYS`.
- **Sprint-10.5 U3 `TenantHub` policy** — Picker holds `hub.connect`; SignalR negotiation succeeds (R13 verification).
- **Sprint-10.5 U4 `NarrowedJwtBuilder` + `OutboundAuthorizationFixture`** — Sprint-11 U3 reuses via MSBuild Compile-link + WAF infrastructure.
- **Sprint-10.5 U5 `usePerm` (reactive) gates** — Sprint-11 U2 wires the 2 new buttons through the same hook.
- **Sprint-10.5 SEC-001 operator-runbook step** — legacy tenants must run `shopflow-migrate seed-owner` before Sprint-11 deploy.
- **Sprint-7 U8 `useOrderMutations.seedOrder` pattern** — Sprint-11 U2 extends with 2 new mutations following the same shape.
- **Sprint-7 U13 `_auth/orders/$orderId.tsx` route** — Sprint-11 U2 adds the button bar.
- **Sprint-3-redux `FulfillmentSaga` + `outbound_saga_transitions`** — Sprint-11 U3 polls the table for state advance verification.
- **Sprint-9 `auth_audit_log`** — storage layer only; NO Sprint-9 command handler currently calls `IAuthAuditLogRepository.AppendAsync` (verified via grep at plan-time per doc-review F3). Sprint-11 U3 does NOT assert auth_audit_log rows; the gap is documented as Sprint-11.5/12 hardening.
- **Auth.Api `Program.cs` does NOT auto-migrate** the tenant DB. PickerFixture must apply Auth schema explicitly via `IModuleMigrationRegistry` before booting Auth.Api WAF; otherwise login throws at EF query layer (per doc-review F4).
- **Docker-backed CI test fixtures** — Sprint-11 U1 integration + U3 E2E both run in CI's nightly + per-PR Docker job. Skip-marked locally per Sprint-1+ posture.

---

## Verification Strategy

Per-unit verification listed in each Implementation Unit. Sprint-wide gates aggregated:

1. **Build**: `dotnet build ShopFlow.sln` → 0 errors + 0 warnings across all 47 projects. Enforced unit-by-unit + at sign-off.
2. **Backend unit tests**: `tests/ShopFlow.Migrate.UnitTests/` includes the new Picker baseline assertion. Sprint-10.5 baseline preserved.
3. **Backend integration tests (filesystem-tier)**: Sprint-10.5 U2 `AdminTsCatalogContractTests` continues to pass (Picker's 4 keys are already in `admin.ts` per Sprint-10.5 U1).
4. **Backend integration tests (Docker-tier)**: `tests/ShopFlow.Migrate.IntegrationTests/` adds Picker-row + idempotency scenarios. `tests/ShopFlow.Outbound.IntegrationTests/Picker/PickerHappyPathTests.cs` adds the new E2E fact. All Skip locally; CI runs full suite.
5. **Frontend Vitest**: U2's `useOrderMutations` test extensions + `$orderId.tsx` route gating tests pass. Sprint-9.5 baseline (~480 passing / 4 pre-existing Sprint-7 a11y failures unchanged) preserved + ~7-10 new tests.
6. **A11y smoke harness**: Sprint-7+ baseline preserved across the button-bar visible + hidden state variants.
7. **CSharpier**: `dotnet csharpier --check .` passes locally (Husky pre-commit) and in CI.
8. **Manual smoke** (if Docker available during Sprint-11 dev): un-Skip the U3 E2E test, run, verify the chain. If Docker not available: rely on Sprint-9.5 U9 + Sprint-10.5 U4 infrastructure proof + CI's Docker-backed run.
9. **Tag + push**: `v0.15.0-sprint-11` annotated tag at U4 HEAD; branch + tag pushed to origin.

---

## Outstanding Questions

### Resolve Before Planning

- None — all blocking decisions resolved during brainstorm + plan-time R8 button-vs-defer question + plan-time doc-review walkthrough.

### Deferred to Implementation

- **[Affects U2][API wrapper presence]** Whether `web/src/api/orders.ts` already has `confirmPickOrder` + `markPickFailed` wrapper functions, or U2 needs to add them. Verify by reading the file first; add if missing.
- **[Affects U3][Dual-WAF Program disambiguation spike]** U3 implementer runs a 30-min spike at unit-start to validate that `extern alias` treatment for `WebApplicationFactory<Program>` against both Auth.Api + Outbound.Api csprojs disambiguates cleanly without CS0433 (Sprint-10.5 U4 hit this on a single-csproj reference and pivoted to MSBuild Compile-link). Spike outcome picks one of two documented paths in U3 Approach: (a) dual-WAF with extern alias + real `/api/auth/login` flow, OR (b) single-WAF + Picker JWT minted via NarrowedJwtBuilder (drops "Picker logs in via real auth endpoint" verification but preserves saga + audit chain).
- **[Affects U3][FulfillmentSagaState table shape]** The exact table/column shape MT uses for the saga state row that U3 must INSERT (to put the order into `AwaitingPick`). Implementer reads Sprint-3-redux MT saga storage config (`OutboundDbContext` + MT EF saga repository registration) to identify the table + column names + correlation_id binding before writing U3's DbContext-seed code.
- **[Affects U3][JWT decode strategy]** Recommend parse-only (no signature verify); JwtBearer downstream catches signature issues. Documented in U3 Approach.

Each of these is execution-time discovery — answerable by reading code or running grep at the moment of implementation, not by additional planning research.
