---
title: Sprint-13 — Packer fourth role + 4-role hand-off proof + MarkPackFailed Path D
type: feat
status: active
date: 2026-05-26
origin: docs/brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md
---

# Sprint-13 — Packer fourth role + 4-role hand-off proof + MarkPackFailed Path D

## Summary

Seven implementation units (U0-U6) land the Packer role end-to-end: domain enum + DB CHECK widening migration, three-key `PackerBaseline` mirroring `DispatcherBaseline`, `MarkPackFailed` endpoint + `PackFailed` saga event + Path D compensation reusing Sprint-12.5's existing primitives, Sprint-12 `HandoffFixture` extended in-place to a 4-role JWT surface (no parallel fixture), and a four-role hand-off E2E + extended `CrossRoleDenialTests` that graduates the adversarial-F3 ordering invariant to its third pin. Backend-only; mirrors Sprint-12 U0-U6 cadence.

---

## Problem Frame

The origin requirements doc establishes the product problem (Owner does too much in operations; saga-failure-path asymmetry at Pack; cross-role denial surface not yet exercised under three non-Owner roles). The plan-side framing: every primitive Sprint-13 needs already exists from Sprint-9 through Sprint-12.5 — this plan composes them rather than introducing new architectural shapes. The lead risk is a brainstorm factual gap analogous to Sprint-12.5's R9 BLOCKING fix (see K1) which the plan corrects up front.

See [docs/brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md](../brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md) for the Problem Frame, Actors, Flows, and Requirements detail carried into this plan.

---

## Requirements

Plan carries forward all 15 origin requirements unchanged:

- R1. Add `Packer` to `UserRole` enum (4th member, index 3 — see K9).
- R2. `UserRoleTests` pins `Enum.GetNames<UserRole>()` against `{Owner, Picker, Dispatcher, Packer}`.
- R3. New Auth migration extends `users.role` CHECK constraint to include `Packer`. **Plan correction:** migration also extends `role_permissions.role` CHECK constraint (see K2 — repo research found Sprint-9 ships TWO CHECK constraints on the same enum string set; both must widen).
- R4. `PackerBaseline` in `RolePermissionsSeed` (3 keys: `OutboundOrdersRead` + `OutboundOrdersPackConfirm` + `HubConnect`).
- R5. `provision` + `seed-owner` write Packer baseline. ADDITIVE-ONLY contract (K7).
- R6. `RolePermissionsSeedTests` extends with Packer baseline + isolation guards for Picker and Dispatcher.
- R7. 4-role hand-off E2E test (Picker → Packer → Dispatcher) within 30-second timeout.
- R8. `MarkPackFailed` endpoint + DTO + saga event + Path D clause. **Plan correction:** saga clause is `During(Picked, When(PackFailed))`, NOT `During(AwaitingPack, ...)` (see K1).
- R9. `Order.MarkCompensatingReservation` allow-set widens to include `Picked`. **Plan correction:** widening target is `Picked`, NOT `AwaitingPack` (see K1).
- R10. Cross-role denial test extension for Packer scenarios + adversarial-F3 third pin + adversarial-F8 union-of-perms pin.
- R11. `NarrowedJwtBuilder.BuildPackerJwt` slots in alongside existing Picker/Dispatcher/Owner JWT builders.
- R12. `actor_user_id` propagation through `PackFailed` event into `outbound_saga_transitions`.
- R13. `dotnet build ShopFlow.sln` — 0 errors + 0 warnings.
- R14. Backend unit-test growth ~20-30 new facts.
- R15. 2-3 new Skip-marked Docker integration tests added; CI runs unskipped.

**Origin actors:** A1 (Owner — unchanged), A2 (Picker — unchanged), A3 (Packer — NEW), A4 (Dispatcher — unchanged).
**Origin flows:** F1 (4-role hand-off Pick → Pack → Ship), F2 (MarkPackFailed Path D), F3 (cross-role denial extended).
**Origin acceptance examples:** AE1 (34-row provisioning), AE2 (4-role happy-path saga drive), AE3-AE6 (MarkPackFailed two-tier guard + DoS guard), AE7-AE8 (cross-role denial + adversarial-F3 ordering pin), AE9 (ADDITIVE-ONLY contract preservation), AE10 (CHECK constraint accepts Packer), AE11 (RolePermissionsSeedTests Packer baseline pin).

---

## Scope Boundaries

Carried unchanged from the origin brainstorm:

- **Frontend stays out.** No `<ConfirmPackButton>`, no `<MarkPackFailedModal>`, no Dispatcher UI views, no frontend MarkShipFailed button. Frontend asymmetry is an accepted Sprint-13 trade-off.
- **Production-hardening pre-work stays out.** Picker / Packer / Dispatcher MFA enforcement, force-change-on-first-login, 4-handler audit catalog expansion, background-channel audit dispatcher.
- **Phase-3 polish stays out.** Observability dashboards (including per-role denial rates with Packer), `auth_audit_log` partitioning, KMS/Vault TOTP KEK migration, `CREATE INDEX CONCURRENTLY` review, PgBouncer pool re-validation.
- **No 25th permission key.** `outbound.orders.pack-confirm` gates both `confirm-pack` AND `mark-pack-failed` (K3).
- **`auth_audit_log` write-path on Outbound saga handlers stays unwired.** Sprint-13's new `MarkPackFailedHandler` continues the pre-existing pattern; actor visibility relies on `actor_user_id` column instead.
- **Owner keeps `outbound.orders.pack-confirm`.** ADDITIVE-ONLY KTD1 contract preserved (K7).
- **`ConfirmPackAsync` controller logic stays unchanged.** Sprint-13 just lets a Packer call it.
- **No Packer-specific "My pack queue" filtered list.**

### Deferred to Follow-Up Work

- **Frontend MarkPackFailed + ConfirmPack buttons + Dispatcher UI views**: Sprint-13.5 or Sprint-14.
- **`HandoffFixture` / `PickerFixture` consolidation** (Sprint-12 KTD4 deferred): Sprint-13 doubles down on `HandoffFixture` extension; consolidation candidate Sprint-14+.
- **3 candidate `/ce-compound` targets** flagged by learnings researcher (UserRole CHECK widening pattern; HandoffFixture+NarrowedJwtBuilder Compile-link; ADDITIVE-ONLY contract) — capture as solution notes after Sprint-13 lands.

---

## Context & Research

### Relevant Code and Patterns

**Sprint-12.5 MarkShipFailed canonical template (mirror for MarkPackFailed):**
- DTO: `src/Services/Outbound/ShopFlow.Outbound.Api/Contracts/OrderDtos.cs:83` — `MarkShipFailedRequest([property: MaxLength(1000)] string? Reason)`.
- Controller action: `src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs:822-898` — `MarkShipFailedAsync`. Policy `[Authorize(Policy = PermissionKeys.OutboundOrdersShipConfirm)]`, two-tier guard, controller-level `Length > 1000` defence-in-depth, calls `order.MarkCompensatingReservation()` + `_publishEndpoint.Publish(new ShipFailed(...))`.
- Saga event record: `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/Events/ShipFailed.cs` — `public sealed record ShipFailed(Guid OrderId, string Reason, Guid? ActorUserId = null)`.
- Saga clause: `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/FulfillmentSaga.cs:213-232` — `During(Packed, When(ShipFailed)) .TransitionTo(CompensatingReservation) .ThenAsync(RecordTransitionAsync(...))`.
- Event declaration: `FulfillmentSaga.cs:58` (`Event(() => ShipFailed, x => x.CorrelateById(...))`) + property at line 363.
- Compensation activity reuse: `FulfillmentSaga.cs:248-276` — `WhenEnter(CompensatingReservation, x => x.IfElse(...))` Path B activity that Path C consumes unchanged. Sprint-13 Path D consumes it unchanged too (K4).

**Sprint-11/12 RolePermissionsSeed canonical template (mirror for PackerBaseline):**
- File: `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs:36-63` — `PickerBaseline` (4 keys), `DispatcherBaseline` (3 keys, no InventoryRead).
- `InsertAsync` helper: lines 122-138 — `(NpgsqlConnection, NpgsqlTransaction, string role, string key, ct)`. SQL: `INSERT ... ON CONFLICT (role, permission_key) DO NOTHING`.
- `SeedAsync` consumption: lines 78-120 — sequential foreach loops over `PermissionKeys.All` / `PickerBaseline` / `DispatcherBaseline`. Log statement at end needs `{PackerCount}` placeholder added.
- DI registration: `tools/shopflow-migrate/Program.cs:140`.

**Sprint-9 UserRole + DB CHECK current state:**
- Enum: `src/Services/Auth/ShopFlow.Auth.Domain/UserRole.cs` — currently `{Owner, Picker, Dispatcher}`. XML doc explicitly anticipates Sprint-9+ widening.
- `users.role` CHECK: `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260520000001_AddUsers.cs:82-86` — `chk_users_role CHECK (role IN ('Owner', 'Picker', 'Dispatcher'))`.
- `role_permissions.role` CHECK: `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260601000001_AddSprint9AuthSchema.cs:129-132` — `chk_role_permissions_role CHECK (role IN ('Owner', 'Picker', 'Dispatcher'))`. Sprint-13 migration must widen BOTH.
- Tests: `tests/ShopFlow.Auth.UnitTests/Domain/UserRoleTests.cs` — `HasExactlyThreeMembers`, `MembersAreOwnerPickerDispatcher`, Theory cases, `OwnerIsTheDefaultValue` (pins index 0).

**Order.MarkCompensatingReservation current allow-set:**
- File: `src/Services/Outbound/ShopFlow.Outbound.Domain/Order.cs:241-257`. Currently accepts `Reserved`, `AwaitingPick`, `AwaitingShip`. Sprint-13 adds `Picked` (NOT `AwaitingPack` — K1).
- Existing test `MarkCompensatingReservation_FromPacked_FailsInvalidState` (line 316) stays unchanged — `Packed` is a transient never-at-rest state.

**Sprint-12 HandoffFixture (extension target):**
- File: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs` (361 lines, standalone — not extending `MultiTenantOutboundFixture`).
- JWT builders: `BuildOwnerJwt`, `BuildPickerJwt`, `BuildDispatcherJwt`, `BuildPickerWithExtraShipConfirmJwt` (Sprint-12 adversarial-F8 builder).
- User IDs + emails: `OwnerUserId`, `PickerUserId`, `DispatcherUserId`. Sprint-13 adds `PackerUserId` + `PackerEmail` + `BuildPackerJwt`.
- Compile-link csproj entries: `tests/ShopFlow.Outbound.IntegrationTests/ShopFlow.Outbound.IntegrationTests.csproj:55-69` (NarrowedJwtBuilder + RolePermissionsSeed both Compile-linked). No csproj change required for Sprint-13 — PackerBaseline lands inside the linked file.
- Hand-off tests: `HandoffWorkflowTests.cs` (Sprint-12 Owner-as-Packer happy-path stays untouched), `CrossRoleDenialTests.cs` (6 Skip-marked facts; Sprint-13 extends).

**OrdersController ctor shape:**
- File: `src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs:129-188` — 9-arg primary `[ActivatorUtilitiesConstructor]` + 7-arg backward-compat. Sprint-13 requires NO new ctor dependencies — `MarkPackFailedAsync` reuses the same deps `MarkShipFailedAsync` consumes.

### Institutional Learnings

- [`docs/solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md`](../solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md) — pre-anticipates Sprint-13 as the third adversarial-F3 pin. Sprint-13 graduates the invariant to "every policy-gated saga-touching endpoint" (K11).
- [`docs/solutions/2026-05-26-jwt-subject-accessor-on-controller-path.md`](../solutions/2026-05-26-jwt-subject-accessor-on-controller-path.md) — `IRequestContext.UserId` is canonical for `MarkPackFailedAsync` actor capture; mirrors Sprint-12.5 KTD4.
- [`docs/solutions/2026-05-10-ef-migration-needs-attributes.md`](../solutions/2026-05-10-ef-migration-needs-attributes.md) — Sprint-13 migration must carry BOTH `[Migration]` + `[DbContext(typeof(AuthDbContext))]`. Silent-no-op trap (K2).
- [`docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md`](../solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md) — Do NOT generate a model snapshot for the Sprint-13 migration. Verify `AuthDbContext.OnConfiguring` ignores `PendingModelChangesWarning`; add if absent.
- [`docs/solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md`](../solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md) — Before adding `PackFailed`, grep for `PickFailed`/`ShipFailed` consumer + test ctors. The positional-default `Guid? ActorUserId = null` (K6) keeps existing fixtures compiling unchanged.
- [`docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md`](../solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md) — Path D MUST NOT touch the Inventory reservation CTE. Hard-won concurrency correctness; reuse downstream `ReleaseLinesAsync` unchanged.

### External References

None — Sprint-13 composes existing primitives; no new framework patterns introduced.

---

## Key Technical Decisions

- **K1. Order.Status is `Picked` (NOT `AwaitingPack`) when MarkPackFailed fires.** Saga clause is `During(Picked, When(PackFailed))`. `Order.MarkCompensatingReservation` allow-set widens to include `Picked`. The Order aggregate never sits in `AwaitingPack` at rest — `ConfirmPackAsync` chains `MarkPacked() → MarkAwaitingShip()` atomically per Sprint-12 KTD2. **Rationale:** Sprint-12.5 R9 documented the analogous correction for Pack→Ship side (`AwaitingShip` was the actual rest state, not `Packed`). Sprint-13's brainstorm carried the symmetric factual gap forward; this plan corrects it as a BLOCKING safe_auto fix before code lands (Sprint-12.5 precedent).
- **K2. Single Auth migration alters BOTH `chk_users_role` AND `chk_role_permissions_role`.** One migration class (`AddPackerRole` or similar) executes DROP-then-ADD for both constraints. **Rationale:** Sprint-9 ships TWO CHECK constraints on the same enum string set; widening only one leaves a latent inconsistency. Mandatory `[Migration]` + `[DbContext(typeof(AuthDbContext))]` attributes per `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`.
- **K3. MarkPackFailed reuses `outbound.orders.pack-confirm` policy — no 25th permission key.** Sprint-12.5 KTD6 precedent. `PermissionKeys.All` stays frozen at 24 keys; `admin.ts` catalog stays frozen.
- **K4. Path D compensation reuses Sprint-12.5 Path B/C primitives unchanged.** `WhenEnter(CompensatingReservation, x => x.IfElse(...))` activity handles `Picked`-entry transparently because `ReservedLineSkus` + `LinesAwaitingRelease` populate on `AwaitingReservation → Reserved` and survive through `Picked`. No new compensation branch.
- **K5. PackerBaseline mirrors DispatcherBaseline shape (3 keys, no `InventoryRead`).** Items already pulled by pack time; smaller blast radius than Picker-shape. Origin decision.
- **K6. `PackFailed` saga event uses positional-default `Guid? ActorUserId = null`.** Sprint-12.5 KTD3 backward-compat pattern preserved — existing fixtures + tests compile unchanged. Mirrors `PickFailed` + `ShipFailed`.
- **K7. Owner KEEPS `outbound.orders.pack-confirm` (ADDITIVE-ONLY KTD1).** Owner remains the do-everything override; Packer added beside Owner, not in place of it. Sprint-11/12 contract preserved.
- **K8. Extend Sprint-12 HandoffFixture in-place — single fixture for happy-path + cross-role denial.** Adds `BuildPackerJwt` + `PackerUserId` + `PackerEmail` to `HandoffFixture.cs`. Sprint-12's existing Owner-as-Packer happy-path stays untouched as regression coverage; new 4-role test sits alongside. **Rationale:** Sprint-12 KTD4 deferred PickerFixture/HandoffFixture consolidation; doubling the fixture for one new role-add multiplies drift surface. Single-fixture extension keeps JWT-builder + user-table seeding in one source.
- **K9. `UserRole.Packer` appended at enum index 3 (last position).** Preserves `Owner=0`, `Picker=1`, `Dispatcher=2` binary serialization ordering — `OwnerIsTheDefaultValue` test continues passing. Existing `UserRole.cs` XML doc-comment (`Sprint-8 ships 3 roles`) updates to reflect 4 roles.
- **K10. Migration timestamp default: `20260527000001`.** Next-day suffix after Sprint-12.5's `20260526000001`. If Sprint-13 U1 lands same-day as Sprint-12.5 sign-off, fall back to `20260526000002`. Final value picked at U1 commit time.
- **K11. Adversarial-F3 ordering pin graduates to third pin** at MarkPackFailed. `Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError` joins the Sprint-12 adversarial-F3 family. `docs/solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md` pre-anticipates this; test naming matches that note's convention. Adversarial-F8 union-of-perms pin: `PickerWithManualPackConfirmGrant_CanPack_BehavioralPin` (Owner-granted-pack-confirm-on-Picker JWT packs successfully; same JWT against ship-confirm returns 403).
- **K12. Version bump: `v0.17.0-sprint-13` (minor).** Matches Sprint-9.5 + Sprint-11 + Sprint-12 precedent for net-new role surface work.

---

## Open Questions

### Resolved During Planning

- **Order.MarkCompensatingReservation allow-set verification** (origin Deferred-to-Planning R9): resolved at plan-time via `src/Services/Outbound/ShopFlow.Outbound.Domain/Order.cs:241-257` read — currently `{Reserved, AwaitingPick, AwaitingShip}`. Sprint-13 adds `Picked` per K1. The `Packed` denial test (`MarkCompensatingReservation_FromPacked_FailsInvalidState` at OrderTests.cs:316) stays unchanged.
- **Migration timestamp pattern** (origin Deferred-to-Planning R3): resolved per K10. Default `20260527000001`; same-day fallback `20260526000002`.
- **HandoffFixture extension vs parallel FourRoleHandoffFixture** (origin Deferred-to-Planning R7): resolved per K8 — extend in place. Sprint-12 KTD4 consolidation candidacy remains open for Sprint-14+.
- **Single CrossRoleDenialTests file vs new PackerCrossRoleDenialTests.cs** (origin Deferred-to-Planning R10): resolved — grow the single file. Sprint-12's 6 facts + Sprint-13's 4 new baseline + 2 adversarial = 12 facts; one file remains manageable. Re-evaluate at Sprint-14+ if a fifth role lands.

### Deferred to Implementation

- **MigrationSmokeTests extension shape**: whether Sprint-13 extends an existing test or adds a new fact pinning the 4-value CHECK constraint reflection. U1 implementer decides based on the test's current parameterization shape.
- **`AuthDbContext.OnConfiguring` PendingModelChangesWarning ignore** (per `docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-...`): U1 verifies presence; adds if absent.
- **Exact wording of MarkPackFailed 409 error code**: plan recommends `order.pack_failure_already_recorded` (mirrors `order.ship_failure_already_recorded` + `order.pick_failure_already_recorded`). U3 implementer confirms against existing string-literal convention.
- **Whether `MarkPackFailedHandler` ships as a separate MediatR command or is inlined in the controller**: depends on whether Sprint-12.5's `MarkShipFailedAsync` is inlined or routes through MediatR. U3 implementer mirrors that exactly.

---

## Implementation Units

### U0. Branch cut + opening commit

**Goal:** Branch from `v0.16.1-sprint-12.5`. Opening commit carries the brainstorm + this plan + the 12 KTDs verbatim in the commit body (Sprint-11/12/12.5 pattern). No code changes yet.

**Requirements:** R1-R15 (orientation only)

**Dependencies:** None.

**Files:**
- Modify: `CLAUDE.md` (current-stage note pointing at Sprint-13 in progress)
- Create: nothing yet
- Test: nothing yet

**Approach:**
- `git checkout -b feat/sprint-13-packer-fourth-role v0.16.1-sprint-12.5`
- Opening commit body includes the 12 KTDs + the BLOCKING K1 factual correction note for any future reader of `git log`.
- Per user memory: push current branch + `v0.16.1-sprint-12.5` tag to origin BEFORE cutting Sprint-13 branch.

**Test expectation:** none — branch-cut commit, no behavioral change.

**Verification:**
- `git log --oneline -1` shows the opening Sprint-13 commit.
- `git diff main..HEAD` shows brainstorm + plan + CLAUDE.md updates only.
- `dotnet build ShopFlow.sln` still returns 0 errors + 0 warnings (no code changes).

---

### U1. UserRole enum + DB CHECK migration + UserRoleTests update

**Goal:** Add `Packer` to the `UserRole` enum (index 3) and ship the Auth migration that widens both `chk_users_role` AND `chk_role_permissions_role` to include `'Packer'`.

**Requirements:** R1, R2, R3, R13.

**Dependencies:** U0.

**Files:**
- Modify: `src/Services/Auth/ShopFlow.Auth.Domain/UserRole.cs` (append `Packer` at index 3 + update XML doc to reflect 4 roles)
- Create: `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260527000001_AddPackerRole.cs` (timestamp per K10; rename if same-day fallback)
- Modify: `tests/ShopFlow.Auth.UnitTests/Domain/UserRoleTests.cs` (update count assertion + add `Packer` to Theory cases + preserve `OwnerIsTheDefaultValue`)
- Verify (read-only, modify if absent): `src/Services/Auth/ShopFlow.Auth.Infrastructure/AuthDbContext.cs` — `OnConfiguring` ignores `PendingModelChangesWarning`

**Approach:**
- Append `Packer` to the enum AFTER `Dispatcher`. K9 preserves `Owner=0/Picker=1/Dispatcher=2` ordering.
- Migration class carries BOTH `[Migration("20260527000001_AddPackerRole")]` AND `[DbContext(typeof(AuthDbContext))]` attributes per K2 + `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`.
- `Up()`: DROP both constraints `IF EXISTS`, then re-ADD with the 4-value set.
- `Down()`: DROP both, re-ADD with the 3-value set (matches Sprint-9 + Sprint-8 prior shape).
- No new column adds; no row backfill needed (Sprint-13 doesn't INSERT any rows with `role = 'Packer'` until U2's provisioning step).

**Execution note:** Test-first — update `UserRoleTests` to assert 4 members BEFORE adding `Packer` to the enum. Red-green cycle pins the agreement contract.

**Patterns to follow:**
- Migration shape: `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260601000001_AddSprint9AuthSchema.cs:127-132` (the `chk_role_permissions_role` ADD precedent).
- Down() shape: `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260520000001_AddUsers.cs:100`.

**Test scenarios:**
- **Happy path** — `UserRoleTests.HasExactlyFourMembers` returns true after `Enum.GetNames<UserRole>().Length == 4`.
- **Happy path** — `UserRoleTests.MembersAreOwnerPickerDispatcherPacker` returns true with set equality.
- **Happy path** — `UserRoleTests.OwnerIsTheDefaultValue` continues passing (`(int)UserRole.Owner == 0`).
- **Edge case** — `[Theory]` cases including new `Packer` entry pass: enum-to-string round-trip and string-to-enum parse symmetric for all 4 values.
- **Integration scenario** — `MigrationSmokeTests` runs the Auth migration chain end-to-end and ends with both CHECK constraints in the 4-value state. **Covers AE10.**

**Verification:**
- `dotnet test --filter "FullyQualifiedName~UserRoleTests"` passes (R2).
- `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings.
- `MigrationSmokeTests` passes against a Testcontainers Postgres tenant DB and confirms `chk_users_role` AND `chk_role_permissions_role` both end at `CHECK (role IN ('Owner', 'Picker', 'Dispatcher', 'Packer'))`.
- A direct test insert with `role = 'Packer'` succeeds; with `role = 'Bogus'` fails.

---

### U2. PackerBaseline in RolePermissionsSeed + tests

**Goal:** Add `PackerBaseline` static readonly list (3 keys) to `RolePermissionsSeed.cs`. Extend `SeedAsync` with a fourth foreach loop. `provision` + `seed-owner` write 34 rows for a fresh tenant (24 Owner + 4 Picker + 3 Packer + 3 Dispatcher).

**Requirements:** R4, R5, R6, R13.

**Dependencies:** U1 (DB CHECK must accept `'Packer'` before any row inserts).

**Files:**
- Modify: `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs` (add `PackerBaseline` + extend `SeedAsync` + update XML doc + update log statement `{PackerCount}` placeholder)
- Modify: `tests/ShopFlow.Migrate.UnitTests/Provisioning/RolePermissionsSeedTests.cs` (extend pinning + isolation guards)
- Modify (compile-link picks it up automatically): `tests/ShopFlow.Outbound.IntegrationTests/Handoff/...` consumers reference `RolePermissionsSeed.PackerBaseline` after this lands — no csproj change

**Approach:**
- `PackerBaseline = new[] { PermissionKeys.OutboundOrdersRead, PermissionKeys.OutboundOrdersPackConfirm, PermissionKeys.HubConnect }` (K5).
- `SeedAsync` gains `foreach (var key in PackerBaseline) { await InsertAsync(conn, tx, "Packer", key, ct); }` — fourth sequential loop after Owner + Picker + Dispatcher.
- Single-transaction shape preserved (existing pattern).
- Log message at end: `{OwnerCount} + {PickerCount} + {DispatcherCount} + {PackerCount} role-permission rows seeded`.

**Patterns to follow:**
- Baseline declaration: `RolePermissionsSeed.cs:36-63` (`PickerBaseline` + `DispatcherBaseline`).
- Loop body: `RolePermissionsSeed.cs:78-120` (`SeedAsync` triple-foreach today).
- Test class layout: `tests/ShopFlow.Migrate.UnitTests/Provisioning/RolePermissionsSeedTests.cs` (Sprint-11/12 baseline-pinning shape).

**Test scenarios:**
- **Happy path** — `PackerBaseline_ContainsExactly_ThreeKeys`: set equality against `{OutboundOrdersRead, OutboundOrdersPackConfirm, HubConnect}`. **Covers AE11.**
- **Happy path** — `PackerBaseline_IsDispatcherShape_NoInventoryRead`: explicit anti-assertion against `InventoryRead`.
- **Integration scenario** — Fresh tenant provisioning end-to-end via `shopflow-migrate provision --tenant=<slug>` against Testcontainers Postgres results in exactly 34 `role_permissions` rows (24 + 4 + 3 + 3). **Covers AE1.**
- **Edge case** — Re-running provision against an already-provisioned tenant adds zero rows (ON CONFLICT DO NOTHING). **Covers AE9 (additive-preservation).**
- **Edge case** — Owner-manual-grant of `outbound.orders.pack-confirm` on `Picker` PRIOR to Sprint-13 deploy: after Sprint-13 provision re-run, BOTH `Picker` AND `Packer` rows hold `outbound.orders.pack-confirm` (Picker's manual grant NOT removed). **Covers AE9.**
- **Security-F1 baseline isolation** — `PickerBaseline_DoesNotContain_OutboundOrdersPackConfirm`: anti-assertion guard.
- **Security-F1 baseline isolation** — `DispatcherBaseline_DoesNotContain_OutboundOrdersPackConfirm`: anti-assertion guard.
- **Security-F1 baseline isolation** — `PackerBaseline_DoesNotContain_OutboundOrdersShipConfirm`: anti-assertion guard.
- **Security-F1 baseline isolation** — `PackerBaseline_DoesNotContain_OutboundOrdersPickConfirm`: anti-assertion guard.

**Verification:**
- `dotnet test --filter "FullyQualifiedName~RolePermissionsSeed"` passes (~10-12 new facts).
- Fresh `shopflow-migrate provision` against a clean tenant DB produces 34 `role_permissions` rows.
- Re-run produces 0 new rows.

---

### U3. MarkPackFailed endpoint + DTO + saga PackFailed event + Path D + Order allow-set widening

**Goal:** Ship the failure-path symmetric to Sprint-12.5's MarkShipFailed: new DTO, new controller action gated by the existing pack-confirm policy, new `PackFailed` saga event, new `During(Picked, When(PackFailed))` clause transitioning to `CompensatingReservation`, widened `Order.MarkCompensatingReservation` allow-set including `Picked`.

**Requirements:** R8, R9, R12, R13, R14.

**Dependencies:** U0. (Independent of U1/U2 — endpoint compiles + tests run before role exists; U4/U5 happy- and negative-path Docker E2E tests gate on U1+U2 + this unit landing.)

**Files:**
- Create: `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/Events/PackFailed.cs`
- Modify: `src/Services/Outbound/ShopFlow.Outbound.Api/Contracts/OrderDtos.cs` (add `MarkPackFailedRequest`)
- Modify: `src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs` (add `MarkPackFailedAsync` action)
- Modify: `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/FulfillmentSaga.cs` (add `Event<PackFailed>` declaration + `During(Picked, When(PackFailed))` clause + property)
- Modify: `src/Services/Outbound/ShopFlow.Outbound.Domain/Order.cs` (`MarkCompensatingReservation` allow-set widens to include `Picked` per K1)
- Modify: `tests/ShopFlow.Outbound.UnitTests/Domain/OrderTests.cs` (add Picked-transition test; preserve Packed-rejection test)
- Modify: `tests/ShopFlow.Outbound.UnitTests/Sagas/FulfillmentSagaTests.cs` (add Path D unit harness fact)
- Modify: `tests/ShopFlow.Outbound.UnitTests/Contracts/MarkPackFailedRequestTests.cs` (or extend MarkShipFailed's test class) — DTO MaxLength validation

**Approach:**
- `PackFailed` event record mirrors `ShipFailed` verbatim: `public sealed record PackFailed(Guid OrderId, string Reason, Guid? ActorUserId = null)` (K6).
- `MarkPackFailedRequest` mirrors `MarkShipFailedRequest`: `public sealed record MarkPackFailedRequest([property: MaxLength(1000)] string? Reason)`.
- Controller action shape mirrors `MarkShipFailedAsync` at `OrdersController.cs:822-898`:
  - `[HttpPost("{id:guid}/mark-pack-failed")]`
  - `[Authorize(Policy = PermissionKeys.OutboundOrdersPackConfirm)]` (K3 — no new key)
  - Two-tier guard: `CompensatingReservation`/`Cancelled` → 409 `order.pack_failure_already_recorded`; non-`Picked` → 422 `order.invalid_state`.
  - Controller-level `Length > 1000` defence-in-depth → 400 `order.reason_too_long`.
  - Reads `IRequestContext.UserId` (mirrors `docs/solutions/2026-05-26-jwt-subject-accessor-on-controller-path.md`).
  - Calls `order.MarkCompensatingReservation()` + `_uow.SaveChangesAsync(ct)` + `_publishEndpoint.Publish(new PackFailed(order.Id, reason, _requestContext.UserId), ct)`.
- Saga clause mirrors `During(Packed, When(ShipFailed))` at `FulfillmentSaga.cs:213-232`, but for `Picked` state per K1:
  - `Event(() => PackFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId))` declaration.
  - `During(Picked, When(PackConfirmed) ..., When(PackFailed) .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow) .TransitionTo(CompensatingReservation) .ThenAsync(ctx => RecordTransitionAsync(ctx, "Picked", "CompensatingReservation", nameof(PackFailed), ctx.Message.ActorUserId)))`.
  - Path D compensation activity REUSES `WhenEnter(CompensatingReservation, x => x.IfElse(...))` at `FulfillmentSaga.cs:248-276` unchanged (K4).
- `Order.MarkCompensatingReservation` allow-set widens to include `Picked` (K1):
  - Current: `Status != Reserved && Status != AwaitingPick && Status != AwaitingShip`
  - After: `Status != Reserved && Status != AwaitingPick && Status != AwaitingShip && Status != Picked`
  - `MarkCompensatingReservation_FromPacked_FailsInvalidState` (existing test) STAYS UNCHANGED — `Packed` is never-at-rest in saga's happy path.

**Execution note:** Test-first. Write the `Order.MarkCompensatingReservation_FromPicked_TransitionsOk` test BEFORE widening the allow-set. Write the `MarkPackFailedRequest` MaxLength validation test BEFORE adding the DTO.

**Technical design (directional, not implementation specification):**

```
Order.Status / Saga state walk-through (K1 correction):

  AwaitingReservation → Reserved → AwaitingPick → Picked
                                                     │
                                          confirm-pick fires (Picker)
                                                     │
                                                     ▼
                                                  Picked  ← MarkPackFailed fires HERE
                                                     │       (Packer)
                                                     │       Order.Status = Picked
                                                     │       Saga state    = Picked
                                                     │       Path D entry
                                                     ▼
                                          CompensatingReservation (reuses Path B/C activity)
                                                     │
                                                     ▼
                                                  Cancelled

  ─── DO NOT route through ─────────────────────────────────────────────
       AwaitingPack  (transient; Order aggregate never sits here at rest;
                      ConfirmPackAsync chains MarkPacked → MarkAwaitingShip
                      atomically)
```

**Patterns to follow:**
- DTO: `MarkShipFailedRequest` at `OrderDtos.cs:83`.
- Controller action: `MarkShipFailedAsync` at `OrdersController.cs:822-898`.
- Saga event: `ShipFailed.cs` (and sibling `PickFailed.cs`) at `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/Events/`.
- Saga clause: `During(Packed, When(ShipFailed))` at `FulfillmentSaga.cs:213-232`.

**Test scenarios:**
- **Happy path** — `MarkCompensatingReservation_FromPicked_TransitionsOk`: order in `Picked` state, call `MarkCompensatingReservation()`, status becomes `CompensatingReservation`. **Covers AE3.**
- **Edge case (preserved)** — `MarkCompensatingReservation_FromPacked_FailsInvalidState`: unchanged from Sprint-12.5; Packed is never-at-rest.
- **Error path** — `MarkPackFailedRequest_ReasonOver1000Chars_FailsValidation`: model-state validation rejects via attribute. **Covers AE6.**
- **Error path** — `MarkPackFailedAsync_OrderInCompensatingReservation_Returns409_AlreadyRecorded`: idempotency via natural-409. **Covers AE4.**
- **Error path** — `MarkPackFailedAsync_OrderInPacked_Returns422_InvalidState`: pre-state guard. **Covers AE5.**
- **Happy path** — `MarkPackFailedAsync_OrderInPicked_PublishesPackFailedEvent_AndOrderInCompensatingReservation`: full controller round-trip via in-memory mocks (NSubstitute). Asserts `IPublishEndpoint.Publish<PackFailed>` was called with `ActorUserId = IRequestContext.UserId`.
- **Integration (saga unit harness)** — `FulfillmentSaga_During_Picked_PackFailed_TransitionsTo_CompensatingReservation`: MassTransit `InMemoryTestHarness` drives saga from `Picked` to `CompensatingReservation` on `PackFailed` consume. Asserts `saga_state.CurrentState` ends at `CompensatingReservation`. Sprint-12.5 Path C harness fact pattern.
- **Integration (saga unit harness)** — `FulfillmentSaga_PathD_Compensation_ReleasesReservedLineSkus`: same harness asserts `ReleaseStockV1` is published with the correct `ReservedLineSkus` minus `ReleasedLineSkus` set.
- **Integration (auth)** — `MarkPackFailedAsync_WithoutPackConfirmPolicy_Returns403`: unit-level WAF integration test asserting `[Authorize(Policy)]` attribute is present (without policy → 403). The Docker-backed E2E lives in U5.

**Verification:**
- `dotnet test --filter "FullyQualifiedName~OrderTests&Category!=Integration"` passes including new `_FromPicked_` fact.
- `dotnet test --filter "FullyQualifiedName~FulfillmentSagaTests&Category!=Integration"` passes including new Path D saga unit harness facts.
- `dotnet test --filter "FullyQualifiedName~MarkPackFailedRequest&Category!=Integration"` passes.
- `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings.

---

### U4. HandoffFixture 4-role extension + 4-role happy-path E2E + MarkPackFailed E2E

**Goal:** Extend Sprint-12's `HandoffFixture` in place to support a 4th JWT (Packer). Ship two new Skip-marked Docker-backed integration tests: 4-role happy-path (Picker → Packer → Dispatcher saga drive) and MarkPackFailed Path D Docker E2E.

**Requirements:** R7, R11, R12, R15.

**Dependencies:** U1, U2, U3.

**Files:**
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs` (add `PackerUserId`, `PackerEmail`, `BuildPackerJwt`; extend users-table seed comments at lines ~158-176)
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs` (add 4-role happy-path Fact; Sprint-12's Owner-as-Packer existing fact stays unchanged for regression coverage)
- Create: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/PackFailedE2ETests.cs` (new file, Skip-marked Docker-backed)

**Approach:**
- Add `public Guid PackerUserId { get; } = Guid.NewGuid();` + `public string PackerEmail => "packer@handoff-tenant.test";` to `HandoffFixture`.
- Add `BuildPackerJwt` mirroring `BuildPickerJwt`/`BuildDispatcherJwt` shape; references `RolePermissionsSeed.PackerBaseline`. Sprint-12's compile-link to `RolePermissionsSeed.cs` picks up `PackerBaseline` automatically — no csproj change.
- CI-tier users-table inserts (commented at fixture lines 158-176): add a 4th INSERT for Packer.
- New 4-role test: Picker `confirm-pick` → Packer `confirm-pack` → Dispatcher `confirm-ship` → saga `Shipped` within 30s timeout (Sprint-11 KTD5 + Sprint-12 KTD7 timeout pattern). Owner JWT is NOT used in this flow.
- MarkPackFailed E2E: Picker confirms pick → Packer marks pack failed → saga ends at `CompensatingReservation` → Path B/C compensation activity drives to `Cancelled`.

**Execution note:** Skip-marked locally per Sprint-1+ posture; CI runs unskipped.

**Patterns to follow:**
- Fixture extension: `HandoffFixture.cs` (the full existing file — append-only changes).
- Happy-path test: `HandoffWorkflowTests.cs` (Sprint-12 `HappyPath_AllThreeRolesDriveSagaToShipped` shape; mirror as 4-role).
- Skip reason format: `SkipReason = "Sprint-13 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon"`.

**Test scenarios:**
- **Happy path** — `HappyPath_AllFourRoles_DriveSagaToShipped`: Picker JWT → POST `/confirm-pick` → assert 200; Packer JWT → POST `/confirm-pack` → assert 200; Dispatcher JWT → POST `/confirm-ship` → assert 200; poll `saga_state.CurrentState` → ends at `Shipped` within 30s. **Covers AE2.**
- **Happy path** — `PackerMarksPackFailed_AfterPickConfirm_SagaEndsAtCancelled`: Picker confirms pick → Packer marks pack failed with `Reason = "damaged at pack station"` → poll saga → ends at `Cancelled`; order line SKUs released. **Covers AE3.**
- **Integration** — `MarkPackFailed_OrderInPicked_ActorUserIdPersistsToOutboundSagaTransitions`: assert `outbound_saga_transitions.actor_user_id` row for the `Picked → CompensatingReservation` transition equals `PackerUserId`. Sprint-12.5 R12 actor propagation verification.
- **Edge case** — `MarkPackFailed_CalledTwice_SecondReturns409_AlreadyRecorded`: first call returns 200, second call returns 409 `order.pack_failure_already_recorded`. **Covers AE4.**
- **Edge case** — `MarkPackFailed_OrderInPacked_Returns422_InvalidState`: order driven past Picked to Packed → mark-pack-failed returns 422. **Covers AE5.**
- **Regression** — Sprint-12's Owner-as-Packer happy-path test continues passing (preserved as historical-regression coverage; not deleted).

**Verification:**
- CI runs the full Docker-backed Outbound integration suite and reports zero failures.
- Locally `dotnet test --filter "Category=Integration&FullyQualifiedName~Handoff"` reports the new facts as Skipped (per Sprint-1+ posture).
- Build returns 0 errors + 0 warnings.

---

### U5. CrossRoleDenialTests extension + adversarial-F3 third pin + adversarial-F8 union-of-perms pin

**Goal:** Extend Sprint-12's `CrossRoleDenialTests.cs` with Packer-specific denial scenarios (4 baseline new) + adversarial-F3 ordering third pin + adversarial-F8 union-of-perms pin for Packer + verify Sprint-12's existing 6 facts still hold.

**Requirements:** R10, R15.

**Dependencies:** U1, U2, U3, U4 (BuildPackerJwt + BuildPickerWithExtraPackConfirmJwt available on `HandoffFixture`).

**Files:**
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` (add 8 new facts after the existing 6; total file count goes 6 → 14)
- Modify: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs` (add `BuildPickerWithExtraPackConfirmJwt` for adversarial-F8)

**Approach:**
- All 8 new facts Skip-marked with the U4-style skip reason.
- Use `_fixture.BuildPackerJwt()` for Packer scenarios; use direct DbContext writes to seed `orders.Status` + `saga_state.CurrentState` per Sprint-11/12 precedent.
- Assert HTTP 403 with `ProblemDetails errorCode = "auth.forbidden"` AND saga state unchanged after the rejected request.
- For the adversarial-F3 third pin: the `Packer` JWT + `Cancelled` order + `POST /confirm-pick` returns 403 `auth.forbidden` (NOT 422 `order.invalid_state`). The auth filter fires BEFORE the controller's pre-state check. Matches the naming convention in `docs/solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md`.
- For the adversarial-F8 union-of-perms pin: build a Picker JWT with an extra `outbound.orders.pack-confirm` claim grant. POST `/confirm-pack` succeeds (200). Then same JWT against `/confirm-ship` returns 403. Sprint-12 KTD1 contract behavioral pin.

**Patterns to follow:**
- File layout: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs` (Sprint-12's 6 existing facts).
- JWT-extra-claim builder: `BuildPickerWithExtraShipConfirmJwt` at `HandoffFixture.cs:310-323`.

**Test scenarios:**
- **Baseline denial** — `Packer_AttemptsConfirmPick_OnAwaitingPickOrder_Returns403`. **Covers AE7.**
- **Baseline denial** — `Packer_AttemptsConfirmShip_OnPackedOrder_Returns403`. **Covers AE7.**
- **Baseline denial** — `Packer_AttemptsMarkPickFailed_OnAwaitingPickOrder_Returns403`.
- **Baseline denial** — `Packer_AttemptsMarkShipFailed_OnAwaitingShipOrder_Returns403`.
- **Baseline denial (new endpoint — Sprint-12 had no analogue)** — `Picker_AttemptsMarkPackFailed_OnPickedOrder_Returns403`. `MarkPackFailed` did not exist in Sprint-12; the brainstorm F3 explicitly mandates pinning Picker denial on the new endpoint.
- **Baseline denial (new endpoint — Sprint-12 had no analogue)** — `Dispatcher_AttemptsMarkPackFailed_OnPickedOrder_Returns403`. Same rationale.
- **Adversarial-F3 third pin** — `Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError`: assert response code is `auth.forbidden` (NOT `order.invalid_state`); saga state unchanged. **Covers AE8.** Matches naming in adversarial-F3 solution note.
- **Adversarial-F8 union-of-perms pin** — `PickerWithManualPackConfirmGrant_CanPack_BehavioralPin`: Picker JWT + extra `pack-confirm` claim → POST `/confirm-pack` returns 200 + saga `Packed`; same JWT against `/confirm-ship` returns 403. Documents the ADDITIVE-ONLY KTD1 consequence.

**Verification:**
- CI runs unskipped suite; reports 14 CrossRoleDenialTests facts passing.
- Locally `dotnet test --filter "Category=Integration&FullyQualifiedName~CrossRoleDenialTests"` shows 14 skipped (Sprint-1+ posture).
- All 6 Sprint-12 existing facts remain passing in CI.

---

### U6. Sign-off + Auth AGENTS.md + CHANGELOG + README + CLAUDE.md + tag

**Goal:** Document Sprint-13 in the canonical sign-off shape. Cut annotated tag `v0.17.0-sprint-13`. Update Auth AGENTS.md with Packer baseline note. Update CHANGELOG + README + CLAUDE.md current-stage.

**Requirements:** R13, R14, R15, K12.

**Dependencies:** U1-U5.

**Files:**
- Create: `docs/phase-gates/2026-05-2X-sprint-13-signoff.md` (date is sign-off-commit day)
- Modify: `src/Services/Auth/AGENTS.md` (Sprint-13 Packer baseline + 4-role enum note + production-hardening obligations carry-forward)
- Modify: `CHANGELOG.md` (Sprint-13 entry)
- Modify: `README.md` (current-stage update)
- Modify: `CLAUDE.md` (current-stage rewrite for Sprint-13; Sprint-12.5 history block preserved)
- Modify: `docs/solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md` (advance the "trajectory" section to record Sprint-13 as the third pin landing)
- Tag: `git tag -a v0.17.0-sprint-13 -m "Sprint-13: Packer fourth role + 4-role hand-off + MarkPackFailed Path D"`

**Approach:**
- Sign-off doc mirrors Sprint-12 + Sprint-12.5 shape: branch + tag + brainstorm + plan + KTD list + per-unit notes + verification gate results + carry-forward trade-offs.
- Auth AGENTS.md `## Production-hardening obligations` subsection (Sprint-12.5 addition) updates: Packer joins Picker/Dispatcher in the MFA enforcement carry-forward; force-change-on-first-login carry-forward unchanged.
- CLAUDE.md current-stage block follows Sprint-12.5's verbose pattern (KTDs enumerated, deviations from plan, next-implementation-step menu).
- Annotated tag per K12 (minor bump; matches Sprint-9.5/11/12 precedent).

**Test expectation:** none — documentation + tag, no behavioral change.

**Verification:**
- `dotnet build ShopFlow.sln` → 0 errors + 0 warnings (regression confirmation).
- Full backend unit-test suite passes (Sprint-12.5 baseline 814 + Sprint-13 expected ~835-845 per R14).
- `git log --oneline v0.16.1-sprint-12.5..HEAD` shows the 7 expected commits (U0-U6).
- `git tag -l 'v0.17.0-sprint-13'` returns the new tag.
- Sign-off doc exists at `docs/phase-gates/`.

---

## System-Wide Impact

- **Interaction graph:** New `PackFailed` event integrates into `FulfillmentSaga` via `Event<PackFailed>` declaration + new `During(Picked, When(PackFailed))` clause. Path D reuses the existing `WhenEnter(CompensatingReservation, x => x.IfElse(...))` compensation activity which downstream publishes `ReleaseStockV1` to Inventory's existing `ReleaseLinesAsync` (untouched).
- **Error propagation:** `MarkPackFailed` two-tier guard returns 409 `order.pack_failure_already_recorded` (idempotency) or 422 `order.invalid_state` (pre-state). Controller-level `Length > 1000` defence-in-depth returns 400 `order.reason_too_long`. Policy gate returns 403 `auth.forbidden` BEFORE the controller body runs (adversarial-F3 invariant).
- **State lifecycle risks:** None new — Path D reuses Sprint-12.5 Path C primitives. `Order.Status` and saga state both at `Picked` when `MarkPackFailed` fires; no transient-state-divergence risk like Sprint-12.5's `AwaitingShip`-vs-`Packed` divergence.
- **API surface parity:** New `POST /api/v1/outbound/orders/{id}/mark-pack-failed` joins `mark-pick-failed` + `mark-ship-failed` family. PermissionKeys catalog unchanged (24 keys); `admin.ts` catalog unchanged; frontend perm[] code paths unchanged.
- **Integration coverage:** 4-role HandoffWorkflow E2E + MarkPackFailed Path D E2E + 6 new CrossRoleDenial facts (including the adversarial-F3 third pin and adversarial-F8 union-of-perms pin). Skip-marked locally per Sprint-1+ posture; CI runs unskipped.
- **Unchanged invariants:** 
  - PermissionKeys.All stays at 24 keys (K3).
  - admin.ts catalog drift unchanged.
  - ADDITIVE-ONLY re-seed contract (KTD1) preserved (K7).
  - Owner KEEPS all 24 keys including pack-confirm.
  - `ConfirmPackAsync` controller logic unchanged — Sprint-13 just lets a Packer call it.
  - Inventory reservation CTE primitives untouched (no concurrency oversell regression).
  - Sprint-12 Owner-as-Packer HandoffWorkflow happy-path test continues passing.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Brainstorm's `AwaitingPack` framing leaks into U3's Order/Saga code | K1 codifies the correction up-front; U3 test-first cadence catches divergence (write `_FromPicked_` test before widening allow-set); plan reads against `Order.cs:241-257` and `FulfillmentSaga.cs:200-211` shipped as research. |
| Migration silently no-ops at runtime (missing `[Migration]` / `[DbContext]`) | `docs/solutions/2026-05-10-ef-migration-needs-attributes.md` carry-forward rule; MigrationSmokeTests asserts CHECK constraint actually ends at 4-value state (would catch silent no-op). |
| EF9 PendingModelChangesWarning blocks the migration | U1 verifies `AuthDbContext.OnConfiguring` ignores it; adds if absent per `docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md`. |
| `PackFailed` record breaks existing test ctors that consume sibling events positionally | K6 positional-default `Guid? ActorUserId = null` preserves backward compat; `docs/solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md` rule applied — grep for `PickFailed`/`ShipFailed` consumer ctors before adding. |
| Path D accidentally touches Inventory CTE primitives | `docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md` carry-forward; U3 explicitly does not touch any Inventory file. |
| `HandoffFixture` extension breaks Sprint-12's existing Owner-as-Packer happy-path | K8 extension is append-only on the fixture (new builder + new IDs); existing fixture surface unchanged; Sprint-12 test stays untouched as regression coverage. |
| Adversarial-F3 third pin naming drifts from solution-note convention | K11 references the note explicitly; U5 test name matches `Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError`. |
| Docker daemon not running on dev machine blocks integration test execution | Sprint-1+ posture preserved: Skip-marked locally, CI runs unskipped. Same as Sprint-11/12/12.5. |

---

## Documentation / Operational Notes

- **Sprint-13 `docs/solutions/` candidates** (capture during U6 sign-off if novel learnings emerge):
  - K1 Pack→Picked / Ship→AwaitingShip state-divergence-vs-actual-state pattern crystallized as a class-level invariant (Sprint-12.5 R9 generalized to "saga-state-vs-Order.Status divergence is the canonical sign of a state-machine factual gap").
- **AGENTS.md update** in `src/Services/Auth/`: `## Production-hardening obligations` subsection adds Packer to the MFA-enforcement carry-forward; the 4-handler audit catalog-expansion item carries forward unchanged.
- **No rollout / monitoring changes** — Sprint-13 is purely additive to an already-instrumented surface. Per-role denial-rate dashboards (Phase-3) will pick up `Packer` as a fourth dimension automatically when they ship.
- **Push reminder** (user memory): push current branch + `v0.16.1-sprint-12.5` tag to origin BEFORE cutting the Sprint-13 branch. Same reminder will apply BEFORE cutting Sprint-14 from `v0.17.0-sprint-13`.

---

## Alternative Approaches Considered

- **Parallel `FourRoleHandoffFixture` over extending `HandoffFixture` in place.** Rejected (K8). Sprint-12 KTD4 deferred PickerFixture/HandoffFixture consolidation; building a third parallel fixture for one new role multiplies the drift surface (three JWT builders to keep in sync; three users-table seed blocks; three Compile-link patches). Extension preserves Sprint-12's existing happy-path test as regression coverage and adds the 4-role test alongside.
- **New `outbound.orders.pack-fail` 25th permission key.** Rejected (K3). Sprint-12.5 KTD6 set the precedent that MarkXFailed reuses the XConfirm key; adding a separate `pack-fail` key would require admin.ts catalog churn, RolePermissionsEditor row addition, and operator-runbook update without a corresponding workflow need.
- **`AwaitingPack` as the MarkPackFailed pre-state (per brainstorm wording).** Rejected (K1). The Order aggregate never sits in `AwaitingPack` at rest — `ConfirmPackAsync` chains Pack → AwaitingShip atomically. The actual rest state when pack-fail fires is `Picked`. This is the BLOCKING factual correction Sprint-12.5 R9 caught for the analogous Pack→Ship side; Sprint-13 applies the same correction to the Pick→Pack side.
- **Sprint-13 includes frontend ConfirmPack + MarkPackFailed buttons.** Rejected at brainstorm time (scope envelope = "Packer role only, lean"). Frontend asymmetry (Picker + Dispatcher have UI buttons; Packer doesn't) is an accepted Sprint-13 trade-off; Sprint-13.5 / Sprint-14 closes.

---

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md](../brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md)
- **Sprint-12 plan** (HandoffFixture / CrossRoleDenialTests origins): [docs/plans/2026-05-22-004-feat-sprint-12-second-non-owner-role-plan.md](2026-05-22-004-feat-sprint-12-second-non-owner-role-plan.md)
- **Sprint-12.5 plan** (MarkShipFailed Path C origin): [docs/plans/2026-05-26-001-feat-sprint-12.5-trade-off-closures-plan.md](2026-05-26-001-feat-sprint-12.5-trade-off-closures-plan.md)
- **Sprint-12 sign-off**: [docs/phase-gates/2026-05-22-sprint-12-signoff.md](../phase-gates/2026-05-22-sprint-12-signoff.md)
- **Sprint-12.5 sign-off**: [docs/phase-gates/2026-05-26-sprint-12.5-signoff.md](../phase-gates/2026-05-26-sprint-12.5-signoff.md)
- **Adversarial-F3 solution note** (Sprint-13 third pin target): [docs/solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md](../solutions/2026-05-26-adversarial-f3-policy-vs-prestate-ordering-invariant.md)
- **JWT subject accessor solution note**: [docs/solutions/2026-05-26-jwt-subject-accessor-on-controller-path.md](../solutions/2026-05-26-jwt-subject-accessor-on-controller-path.md)
- **EF migration attributes solution note**: [docs/solutions/2026-05-10-ef-migration-needs-attributes.md](../solutions/2026-05-10-ef-migration-needs-attributes.md)
- **EF9 PendingModelChangesWarning solution note**: [docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md](../solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md)
- **Contracts evolution sweep solution note**: [docs/solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md](../solutions/2026-05-20-contracts-evolution-consumer-test-sweep.md)
- **Inventory CTE primitives solution note (do-not-touch)**: [docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md](../solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md)
- **Canonical source files referenced by the plan:**
  - `src/Services/Auth/ShopFlow.Auth.Domain/UserRole.cs`
  - `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260520000001_AddUsers.cs`
  - `src/Services/Auth/ShopFlow.Auth.Infrastructure/Migrations/20260601000001_AddSprint9AuthSchema.cs`
  - `src/Services/Outbound/ShopFlow.Outbound.Domain/Order.cs:241-257`
  - `src/Services/Outbound/ShopFlow.Outbound.Api/Controllers/OrdersController.cs:822-898`
  - `src/Services/Outbound/ShopFlow.Outbound.Api/Contracts/OrderDtos.cs:75,83`
  - `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/FulfillmentSaga.cs:213-232`
  - `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/Events/ShipFailed.cs`
  - `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs:36-63,78-138`
  - `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs`
  - `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs`
  - `tests/ShopFlow.Auth.UnitTests/Domain/UserRoleTests.cs`
