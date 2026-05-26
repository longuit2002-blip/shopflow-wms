---
title: Sprint-13 — Packer fourth role + 4-role hand-off proof + MarkPackFailed Path D
created: 2026-05-26
status: ready-for-planning
origin: solo-brainstorm
actors: [Owner, Picker, Packer, Dispatcher]
flows: [F1-pick-pack-ship-handoff-4-role, F2-mark-pack-failed-path-d, F3-cross-role-denial-extended]
---

# Sprint-13 — Packer fourth role + 4-role hand-off proof + MarkPackFailed Path D

## Summary

Add `Packer` as the third non-Owner role with a three-key baseline mirroring Dispatcher, move pack-confirm off Owner, introduce a `MarkPackFailed` endpoint with saga Path D compensation symmetric with Sprint-12.5's Path C, and prove the stack via a 4-role hand-off E2E test (Picker → Packer → Dispatcher).

---

## Problem Frame

Sprint-11 / Sprint-12 / Sprint-12.5 built up a 3-role saga workflow where Pack-confirm is the only transition still held by Owner. The remaining surface gaps are:

1. **Owner does too much in operations.** A real warehouse separates picking, packing, and dispatching across three operators. Today Owner is the only role that can call `confirm-pack`, so any production deployment either (a) hands the Owner credential to a Packer (collapsing role separation) or (b) blocks the workflow at Pack until Owner is available. Both are wrong shapes for a defensible production deployment.

2. **Saga-failure-path asymmetry.** Picker (Sprint-11) has `MarkPickFailed`. Dispatcher (Sprint-12.5) has `MarkShipFailed` with Path C compensation. Owner-as-Packer today has no failure path at Pack — damaged-at-pack-station discovery silently rolls the order forward toward Ship. The role most likely to discover physical damage (the operator handling the items) has the weakest failure-path coverage.

3. **Role-confusion surface not yet exercised under 3 non-Owner roles.** Sprint-12 pinned cross-role denials between Picker, Owner, and Dispatcher. A third non-Owner role exercises whether per-action policies hold when more than one narrowed role exists simultaneously — and whether the ADDITIVE-ONLY contract (KTD1 inherited from Sprint-11) still preserves Owner additions on re-seed when a third baseline lands.

Sprint-13 closes all three by adding `Packer` to the `UserRole` enum + DB CHECK constraint, introducing a `PackerBaseline` of 3 keys (`outbound.orders.read` + `outbound.orders.pack-confirm` + `hub.connect`), wiring `MarkPackFailed` with saga Path D compensation that reuses Sprint-12.5 Path B/C primitives, and proving the stack via 4-role hand-off E2E + extended cross-role denial tests.

---

## Actors

- **A1 — Owner.** Established (Sprint-1 through Sprint-12.5). Owns the 24-key `PermissionKeys.All` superset including `outbound.orders.pack-confirm`. Per ADDITIVE-ONLY contract (KTD1), Owner KEEPS pack-confirm — Packer is added beside Owner, not in place of it. `MfaRequired=true` per Sprint-9 R17 unchanged.
- **A2 — Picker.** Established Sprint-11. 4-key baseline: `outbound.orders.read` + `outbound.orders.pick-confirm` + `inventory.read` + `hub.connect`. Unchanged at Sprint-13.
- **A3 — Packer (new in Sprint-13).** 3-key baseline: `outbound.orders.read` + `outbound.orders.pack-confirm` + `hub.connect`. Dispatcher-shape (no `inventory.read` — items are pulled by pack time). MFA NOT enforced at Sprint-13 (consistent with Picker / Dispatcher; hardening deferred to a future sprint).
- **A4 — Dispatcher.** Established Sprint-12. 3-key baseline: `outbound.orders.read` + `outbound.orders.ship-confirm` + `hub.connect`. Unchanged at Sprint-13.

---

## Key Flows

### F1 — Pick → Pack → Ship 4-role hand-off on one order

One order moves through three saga transitions, each performed by a different non-Owner role's JWT:

1. **Picker** (Sprint-11 baseline) issues `POST /api/v1/outbound/orders/{id}/confirm-pick` → saga transitions `AwaitingPick → Picked`.
2. **Packer** (Sprint-13 baseline, NEW) issues `POST /api/v1/outbound/orders/{id}/confirm-pack` → saga transitions `Picked → Packed`, and Order aggregate chains `MarkPacked() → MarkAwaitingShip()` in one `SaveChangesAsync` per Sprint-12 KTD2 (so `Order.Status` is `AwaitingShip` when the saga state is `Packed`).
3. **Dispatcher** (Sprint-12 baseline) issues `POST /api/v1/outbound/orders/{id}/confirm-ship` → saga transitions `Packed → Shipped`.

All three transitions land on the same `saga_state` row (same `CorrelationId`). No role inherits another role's permissions implicitly. Owner is no longer required for the happy path; Owner retains the ability to perform any step as override.

### F2 — Packer fails a pack at AwaitingPack (Path D compensation)

The Packer encounters a damaged item at the pack station after pick has been confirmed. Saga is in state `AwaitingPack`.

1. **Packer** issues `POST /api/v1/outbound/orders/{id}/mark-pack-failed` with a `Reason` body (max 1000 chars per Sprint-12.5 KTD10) → endpoint gated by existing `outbound.orders.pack-confirm` policy (no new permission key per Sprint-12.5 KTD6).
2. Controller two-tier guard: `CompensatingReservation`/`Cancelled` → 409 `order.pack_failure_already_recorded` (natural-409 idempotency, no header-keyed dedup); non-`AwaitingPack` → 422 `order.invalid_state`.
3. Saga `During(AwaitingPack, When(PackFailed))` clause transitions to `CompensatingReservation`. Existing `WhenEnter(CompensatingReservation)` compensation activity (Sprint-12.5 Path B/C primitives) handles the release of `ReservedLineSkus - ReleasedLineSkus`. No new compensation code — Path D reuses Path B/C unchanged.
4. `Order.MarkCompensatingReservation` allow-set already includes `AwaitingPack` (verify; widen if not — this is the equivalent factual correction Sprint-12.5 made for `AwaitingShip`).

### F3 — Cross-role denial extended for Packer scenarios

Sprint-12 pinned 6 denial scenarios across Picker, Owner, Dispatcher. Sprint-13 extends with Packer-specific denials, including the adversarial-F3 ordering pin (auth filter fires before pre-state check) and an adversarial-F8 union-of-perms pin (Sprint-12 precedent).

Required negative-path scenarios:

- Packer JWT against `POST /api/v1/outbound/orders/{id}/confirm-pick` → 403 `auth.forbidden`.
- Packer JWT against `POST /api/v1/outbound/orders/{id}/confirm-ship` → 403 `auth.forbidden`.
- Packer JWT against `POST /api/v1/outbound/orders/{id}/mark-pick-failed` → 403 `auth.forbidden`.
- Packer JWT against `POST /api/v1/outbound/orders/{id}/mark-ship-failed` → 403 `auth.forbidden`.
- Picker JWT against `POST /api/v1/outbound/orders/{id}/confirm-pack` → 403 (Sprint-12 already covers; reaffirm under 3-non-Owner-role world).
- Picker JWT against `POST /api/v1/outbound/orders/{id}/mark-pack-failed` → 403 `auth.forbidden`.
- Dispatcher JWT against `POST /api/v1/outbound/orders/{id}/confirm-pack` → 403 (Sprint-12 already covers; reaffirm).
- Dispatcher JWT against `POST /api/v1/outbound/orders/{id}/mark-pack-failed` → 403 `auth.forbidden`.
- Adversarial-F3 ordering pin: Packer JWT against `confirm-pick` on an order in state `Cancelled` returns 403 `auth.forbidden` (NOT 422 `order.invalid_state`) — proves `[Authorize(Policy)]` filter fires before the controller's pre-state check.
- Adversarial-F8 union-of-perms behavioral pin: an Owner-granted-pack-confirm-on-Picker JWT can pack-confirm successfully, then the same JWT against `confirm-ship` returns 403. Documents the KTD1 contract's consequence: an Owner who manually grants Picker `pack-confirm` HAS granted pack capability — no defense-in-depth surprise rescue.

---

## Acceptance Examples

- **AE1.** **Covers R1, R2.** A fresh tenant provisioned by `shopflow-migrate provision --tenant=<slug>` has exactly 34 `role_permissions` rows: 24 Owner + 4 Picker + 3 Packer + 3 Dispatcher. The Packer row set is exactly `{outbound.orders.read, outbound.orders.pack-confirm, hub.connect}`.
- **AE2.** **Covers R7.** An order seeded directly to state `AwaitingPick`, then driven through `confirm-pick` (Picker JWT) → `confirm-pack` (Packer JWT) → `confirm-ship` (Dispatcher JWT), ends at saga state `Shipped` within a baked-in 30-second timeout. Each transition returns HTTP 200. Owner is not used at any point in this happy path.
- **AE3.** **Covers R8.** An order at saga state `AwaitingPack`, driven through `mark-pack-failed` (Packer JWT) with a non-empty Reason, ends at saga state `CompensatingReservation` with all reserved line SKUs released (counter drained to zero), then transitions to `Cancelled` via the existing Sprint-12.5 Path B/C compensation activity. Endpoint returns HTTP 200.
- **AE4.** **Covers R8.** A second `mark-pack-failed` call against the same order (already at `CompensatingReservation`) returns HTTP 409 `order.pack_failure_already_recorded`. Natural-409 idempotency, no header-keyed dedup.
- **AE5.** **Covers R8.** A `mark-pack-failed` call against an order at state `Packed` (not `AwaitingPack`) returns HTTP 422 `order.invalid_state`.
- **AE6.** **Covers R8.** A `mark-pack-failed` request with `Reason` exceeding 1000 characters returns HTTP 400 (model validation) per Sprint-12.5 KTD10 precedent applied to both `MarkShipFailedRequest.Reason` and `MarkPickFailedRequest.Reason`.
- **AE7.** **Covers R9.** A Packer JWT issuing `POST /api/v1/outbound/orders/{id}/confirm-pick` returns HTTP 403 with problem-details `errorCode: "auth.forbidden"`. Order saga state is unchanged. Verified in a Docker-backed integration test.
- **AE8.** **Covers R9.** A Packer JWT against `confirm-pick` on an order in state `Cancelled` returns HTTP 403 `auth.forbidden` (NOT 422 `order.invalid_state`). Auth filter fires before pre-state check (adversarial-F3 ordering pin).
- **AE9.** **Covers R10.** An Owner who manually grants `outbound.orders.pack-confirm` to the Picker role via `/admin/role-permissions` (pre-Sprint-13 deploy), then deploys Sprint-13 and re-runs `shopflow-migrate provision`, ends with BOTH Picker AND Packer rows holding `outbound.orders.pack-confirm`. The Owner's manual grant on Picker is NOT removed by Sprint-13's provisioning (KTD1 ADDITIVE-ONLY contract preserved).
- **AE10.** **Covers R3.** The `users.role` DB CHECK constraint after Sprint-13 migration accepts inserts with `role = 'Packer'`. Inserts with values outside `{Owner, Picker, Dispatcher, Packer}` are rejected with constraint-violation. The CHECK constraint is pinned to the `UserRole` enum by `UserRoleTests.cs`.
- **AE11.** **Covers R4, R5.** The `RolePermissionsSeedTests` test class gains new facts pinning the `PackerBaseline` static readonly list contents (3 keys, named explicitly) AND a security-F1 isolation guard `PickerBaseline_DoesNotContain_OutboundOrdersPackConfirm` AND `DispatcherBaseline_DoesNotContain_OutboundOrdersPackConfirm`.

---

## Requirements

**Domain + migration**

- **R1.** Add `Packer` to the `UserRole` enum in `src/Services/Auth/ShopFlow.Auth.Domain/UserRole.cs` (4th enum member). XML doc-comment listing roles updated to `('Owner', 'Picker', 'Dispatcher', 'Packer')`.
- **R2.** `UserRoleTests.cs` gains a fact pinning that `Enum.GetNames<UserRole>()` exactly equals `{"Owner", "Picker", "Dispatcher", "Packer"}` — the DB-CHECK / enum agreement contract.
- **R3.** New migration extending `users.role` CHECK constraint to `CHECK (role IN ('Owner', 'Picker', 'Dispatcher', 'Packer'))`. DROP-then-ADD pattern (Sprint-9 precedent in `20260601000001_AddSprint9AuthSchema.cs`). Mandatory `[Migration]` + `[DbContext]` attributes per AGENTS.md.

**Provisioning + baseline**

- **R4.** Add `PackerBaseline` static readonly list to `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` with the 3 canonical `PermissionKeys` constants: `OutboundOrdersRead`, `OutboundOrdersPackConfirm`, `HubConnect`. Shared `InsertAsync` helper (Sprint-11/12 pattern) is reused for the Packer loop. Class XML doc updated to reflect 4-role state.
- **R5.** `shopflow-migrate provision` and `shopflow-migrate seed-owner` both extend to write the Packer baseline rows alongside Owner + Picker + Dispatcher. Idempotent re-runs (`ON CONFLICT (role, permission_key) DO NOTHING`) preserve Owner additions across re-seed; Owner deletions from the Packer baseline REVERT on the next provision run. Same ADDITIVE-ONLY contract as Sprint-11 KTD1.
- **R6.** `RolePermissionsSeedTests` (Sprint-11/12 baseline) gains new facts: Packer-specific baseline contents pin (3 keys named explicitly), security-F1 baseline-isolation guards for both Picker and Dispatcher (neither contains `outbound.orders.pack-confirm`), and additive-preservation scenarios mirroring Sprint-12 AE6.

**Saga happy path (Packer confirms pack)**

- **R7.** `ConfirmPack` endpoint policy unchanged — already `[Authorize(Policy = PermissionKeys.OutboundOrdersPackConfirm)]` since Sprint-10. Packer's 3-key baseline grants this policy, so no controller-side change is required for happy-path enablement. Docker-backed E2E test at `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` (Sprint-12 fixture extends to 4-role flow OR new `FourRoleHandoffFixture` per planner decision) drives the 4-role hand-off on one order. Test asserts each of the 3 transitions returns 200 AND saga state poll converges within 30 seconds (Sprint-11 KTD5 + Sprint-12 KTD7 timeout pattern). Skip-marked locally; CI runs in Docker tier.

**Saga failure path (Packer marks pack failed)**

- **R8.** New endpoint `POST /api/v1/outbound/orders/{id}/mark-pack-failed` gated by existing `outbound.orders.pack-confirm` policy (no new permission key per Sprint-12.5 KTD6). New `MarkPackFailedRequest` DTO with `[MaxLength(1000)]` on `Reason` (Sprint-12.5 KTD10) + controller-level defence-in-depth length guard. Two-tier guard: `CompensatingReservation`/`Cancelled` → 409 `order.pack_failure_already_recorded`; non-`AwaitingPack` → 422 `order.invalid_state`. New `PackFailed` saga event with `Guid? ActorUserId = null` positional-default (Sprint-12.5 KTD3 backward-compat pattern). `During(AwaitingPack, When(PackFailed))` clause transitions to `CompensatingReservation`. Path D reuses Sprint-12.5 Path B/C compensation primitives unchanged.
- **R9.** Verify `Order.MarkCompensatingReservation` allow-set already includes `AwaitingPack`. If not, widen to include it per Sprint-12.5 KTD's analogous correction for `AwaitingShip`. Domain test pinning the allow-set.

**Cross-role denial**

- **R10.** Docker-backed cross-role denial test at `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` extends Sprint-12's 6 scenarios with the Packer denial scenarios listed in F3 (8 new baseline + 1 adversarial-F3 ordering pin + 1 adversarial-F8 union-of-perms pin). Each fact issues a real non-Owner JWT (not a narrowed Owner) and asserts HTTP 403 + saga state unchanged. Skip-marked locally; CI runs in Docker tier.

**Test infrastructure**

- **R11.** `NarrowedJwtBuilder` gains a `BuildPackerJwt(tenant, userId)` method mirroring existing `BuildPickerJwt` / `BuildDispatcherJwt`. MSBuild Compile-link pattern (Sprint-11 KTD4) holds. Used by `HandoffWorkflowTests` + `CrossRoleDenialTests`.
- **R12.** `actor_user_id` propagation: `OrdersController.MarkPackFailedAsync` reads `IRequestContext.UserId` (Sprint-12.5 KTD4 canonical accessor with defensive `ClaimTypes.NameIdentifier` fallback) and includes it in the `PackFailed` saga event publish. `FulfillmentSaga`'s `RecordTransitionAsync` site for the new transition passes the actor through to `outbound_saga_transitions.actor_user_id` (Sprint-12.5 U2/U3 mechanism unchanged).

**Build + verification**

- **R13.** `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings across all projects (same gate as every prior sprint).
- **R14.** Backend unit-test count grows by approximately 20-30 new facts spread across Auth.UnitTests (UserRole enum pin), Migrate.UnitTests (PackerBaseline + isolation guards), Outbound.UnitTests (MarkPackFailedRequest validation, Order domain transitions, saga Path D unit harness). Sprint-12.5 baseline 814 → Sprint-13 expected ~835-845.
- **R15.** 2-3 new Skip-marked Docker-backed integration tests added (HandoffWorkflowTests extended for 4-role; new MarkPackFailed E2E; CrossRoleDenialTests extended). CI runs unskipped.

---

## Success Criteria

- **Human outcome.** A real warehouse can deploy Sprint-13 and run the Pick → Pack → Ship workflow across three separate non-Owner operator accounts. Owner credentials are no longer required for the happy path. A Packer who discovers a damaged item at the pack station has a documented endpoint to fail the pack without escalating to Owner. The 4-role hand-off + cross-role denial coverage is provable end-to-end via the Docker-backed integration test suite.
- **Downstream-agent handoff.** `ce-plan` consuming this brainstorm can write a unit-decomposed plan without inventing: role semantics, baseline contents, saga state transitions, compensation reuse strategy, endpoint policies, JWT builder shape, test layout, version-bump shape, or cross-role denial scenario list. The only items `ce-plan` legitimately invents are: file-level diff shapes, exact migration timestamps, exact KTD numbering for Sprint-13-specific decisions, exact test method names, and Path D compensation activity wiring details (existing primitives — just plug them in).

---

## Scope Boundaries

- **Frontend bits stay out.** No `<ConfirmPackButton>`, no `<MarkPackFailedModal>`, no Dispatcher UI views ("My ship queue"), no frontend MarkShipFailed button. Sprint-13 ships backend-only. Frontend asymmetry (Picker + Dispatcher have UI buttons; Packer doesn't) is an accepted Sprint-13 trade-off carried forward to Sprint-13.5 or Sprint-14.
- **Production-hardening pre-work stays out.** Picker / Packer / Dispatcher MFA enforcement, force-change-on-first-login enforcement, 4-handler audit catalog expansion (the 4 Auth handlers without documented EventType keys per Sprint-12.5 KTD2), and background-channel audit dispatcher for true latency-isolation all defer to a separate Sprint-13+ hardening workstream.
- **Phase-3 polish stays out.** Observability dashboards (per-role denial rates per tenant, including the new Packer denial rates), `auth_audit_log` partitioning + archival, KMS/Vault TOTP KEK migration, `CREATE INDEX CONCURRENTLY` review, PgBouncer pool re-validation. All Phase-3.
- **No 25th permission key.** `outbound.orders.pack-confirm` gates both `confirm-pack` AND `mark-pack-failed`. The 24-key `PermissionKeys.All` catalog stays frozen at Sprint-9 size. Sprint-12.5 KTD6 precedent (MarkShipFailed reused `outbound.orders.ship-confirm`).
- **`auth_audit_log` write-path on Outbound saga handlers stays unwired.** Sprint-12.5 closed audit-log wiring for 12 Auth handlers but did NOT extend to Outbound saga handlers (ConfirmPick / ConfirmPack / ConfirmShip / MarkPickFailed / MarkShipFailed). Sprint-13's new `MarkPackFailedHandler` continues that pattern — no audit-log call. Actor visibility relies on the `actor_user_id` column on `outbound_saga_transitions` (Sprint-12.5 U2/U3 mechanism). Closing the Outbound audit-log gap is a separate workstream.
- **Owner does not lose `outbound.orders.pack-confirm`.** ADDITIVE-ONLY KTD1 contract holds. Owner remains the do-everything role; Packer is added beside Owner, not in place of it.
- **`ConfirmPackAsync` controller logic stays unchanged.** It already chains `MarkPacked() → MarkAwaitingShip()` per Sprint-12 KTD2; Sprint-13 just lets a Packer call it instead of only Owner.
- **No Packer-specific "My pack queue" filtered list.** Sprint-13 does not ship Packer-targeted UI views. Same reasoning as Sprint-11 / Sprint-12 deferring role-specific filtered lists.

---

## Key Decisions

- **Packer baseline = Dispatcher-shape (3 keys, no `inventory.read`).** Items are already pulled by pack time; Packer does not need to query stock-on-hand. Smaller blast radius than Picker-shape. Decision in dialogue.
- **MarkPackFailed reuses `outbound.orders.pack-confirm` policy.** No 25th permission key. Sprint-12.5 KTD6 precedent. Operator who can pack-confirm can also pack-fail; if a future operator-runbook needs to split these, a new key can be carved out later, but Sprint-13 keeps the 24-key catalog frozen.
- **Path D compensation reuses Sprint-12.5 Path B/C primitives unchanged.** No new compensation code path. The existing `WhenEnter(CompensatingReservation, x => x.IfElse(...))` activity handles `AwaitingPack`-entry transparently because `ReservedLineSkus` is populated on `AwaitingReservation → Reserved` and survives through `Picked → AwaitingPack`.
- **Owner keeps pack-confirm.** ADDITIVE-ONLY KTD1 contract preserved. Owner is the do-everything override; non-Owner baselines are narrow.
- **Lean scope (Packer role only, no frontend).** Mirrors Sprint-11 / Sprint-12 cadence. Frontend bits defer to a future point release.
- **Version bump expected: `v0.17.0-sprint-13` (minor).** Matches Sprint-9.5 + Sprint-11 + Sprint-12 precedent for net-new role surface work.

---

## Dependencies / Assumptions

- **`OutboundOrdersPackConfirm` PermissionKey already exists** in `PermissionKeys.All` (Sprint-9 catalog) and is already applied as `[Authorize(Policy = ...)]` on the `ConfirmPack` action (Sprint-10 migration). Sprint-13 attaches the same key to the new `MarkPackFailed` action and to the new Packer baseline.
- **`UserRole.Dispatcher` already exists** in the enum + DB CHECK constraint (Sprint-9 + Sprint-12). Sprint-13 adds `Packer` as the 4th value; this is a real domain + migration change.
- **`outbound_saga_transitions.actor_user_id` column already exists** (Sprint-12.5 U2/U3 additive migration). Sprint-13 reads / writes to it; no schema change needed for actor propagation.
- **`IRequestContext.UserId` is the canonical JWT subject accessor** on Outbound controllers (Sprint-12.5 KTD4). Sprint-13 follows the same pattern in `MarkPackFailedAsync`.
- **MassTransit saga DSL accepts `Initially / During` extensions** for new event types without schema migration on `saga_state`. The `FulfillmentSaga` instance state already carries `ReservedLineSkus` + `LinesAwaitingRelease` + `ReleasedLineSkus` (Sprint-12.5 verification); Path D consumes the same fields.
- **`HandoffFixture` (Sprint-12) is extendable to a 4-role flow** OR a new `FourRoleHandoffFixture` is acceptable. Planner decision; sign-off documents which path was taken. Sprint-12 KTD4 chose `HandoffFixture` parallel to `PickerFixture` rather than generalizing — Sprint-13 may choose either generalization or extension.
- **No frontend test churn expected.** Sprint-13 ships zero frontend changes; Vitest test count holds at Sprint-12 baseline (~549 passing).
- **Sprint-12.5 baseline `Order.MarkCompensatingReservation` allow-set MAY OR MAY NOT include `AwaitingPack`.** R9 verifies this and widens if needed. Sprint-12.5 widened to `AwaitingShip` for MarkShipFailed; Sprint-13 verifies the Pack side.

---

## Outstanding Questions

### Deferred to Planning

- **[Affects R9][Technical]** Does `Order.MarkCompensatingReservation` allow-set already include `AwaitingPack`? Sprint-12.5 widened it for `AwaitingShip` but the Pack side was not explicitly verified at that time. Verifiable at plan-time via grep of `OrderTests` or domain source; R9 is a no-op verification if already widened, or adds the widening if not.
- **[Affects R3][Technical]** Migration timestamp + filename — `ce-plan` determines exact format consistent with Sprint-12.5 precedent (`20260526000001_*`).
- **[Affects R7][Technical]** Whether to extend Sprint-12's `HandoffFixture` to 4-role OR introduce a new `FourRoleHandoffFixture`. Trade-off: extending touches Sprint-12 test infrastructure (lower test-class duplication, higher regression risk); introducing parallel keeps Sprint-12 untouched (Sprint-12 KTD4 precedent). `ce-plan` decides based on Sprint-12 fixture coupling depth.
- **[Affects R10][Technical]** Whether to split `CrossRoleDenialTests` into `CrossRoleDenialTests.cs` (Sprint-12 6 scenarios kept) + `PackerCrossRoleDenialTests.cs` (Sprint-13 8 new + 2 adversarial), or to grow the single file. Plan decides based on file size after Sprint-13 additions.
- **[Affects R14][Needs research]** Whether the Packer role's saga-failure-path Path D needs additional unit test coverage beyond the 3 saga Path C unit facts from Sprint-12.5. Plan decides based on Path D's structural reuse depth.
