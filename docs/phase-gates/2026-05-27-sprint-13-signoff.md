# Sprint-13 Sign-off — Packer fourth role + 4-role hand-off proof + MarkPackFailed Path D

**Date**: 2026-05-27
**Tag**: `v0.17.0-sprint-13`
**Branch**: `feat/sprint-13-packer-fourth-role` (cut from `v0.16.1-sprint-12.5`)
**Plan**: [`docs/plans/2026-05-26-002-feat-sprint-13-packer-fourth-role-plan.md`](../plans/2026-05-26-002-feat-sprint-13-packer-fourth-role-plan.md)
**Brainstorm**: [`docs/brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md`](../brainstorms/2026-05-26-sprint-13-packer-fourth-role-requirements.md)

## Summary

Sprint-13 lands the **Packer** role end-to-end as the third non-Owner role, moving pack-confirm off Owner-only. The 4-role hand-off chain is now **Picker → Packer → Dispatcher** on one saga. A `MarkPackFailed` endpoint + saga Path D compensation ships symmetric to Sprint-12.5's Path C. Backend-only; frontend bits + production-hardening pre-work + Phase-3 polish all deferred per the brainstorm scope envelope ("Packer role only, lean").

**7 of 7 implementation units shipped** (U0-U6), plus a pre-U0 csharpier cleanup commit.

## Verification gates met

- `dotnet build ShopFlow.sln` → **0 errors + 0 warnings** across all projects (R13).
- Backend unit tests: **832 passing** (Sprint-12.5 baseline 814; **+18 net new**) (R14):
  - Auth.UnitTests 210 (was 207; +3 UserRoleTests: HasExactlyFourMembers, PackerAppendsAtIndexThree, Theory expansions)
  - Migrate.UnitTests 71 (was 61; +10 PackerBaseline pins + isolation guards)
  - Outbound.UnitTests 143 (was 138; +5: Order Picked-transition + 3 saga Path D + 1 policy-coverage)
  - Per-project: Analytics 1 / Gateway 1 / ControlPlane 16 / Inbound 28 / SharedKernel 53 / Notification 66 / StockSync 71 / Inventory 74 / Channel 98 / Migrate 71 / Outbound 143 / Auth 210
- New Skip-marked Docker-backed integration tests (R15): HandoffWorkflowTests +1 (4-role happy-path), PackFailedE2ETests +4 (new file), CrossRoleDenialTests +8 (6→14), RolePermissionsSeedIntegrationTests +1 (34-row provisioning + AE9). CI runs the full suite unskipped.

## Implementation units

- **(pre-U0)** `chore(formatting)` — `dotnet csharpier .` on 387 .cs files clearing the Phase-0-redux-flagged drift; `dotnet husky install` re-installed the missing `.husky/_/husky.sh` runtime helper. Unblocks the pre-commit hook for Sprint-13 onward. (`1f703fb`)
- **U0** — Branch cut + brainstorm + plan + 12 KTDs in opening commit body. (`5b35f28`)
- **U1** — `UserRole.Packer` at enum index 3 (K9) + migration `20260527000001_AddPackerRole` widening BOTH `chk_users_role` AND `chk_role_permissions_role` (K2) + UserRoleTests (210 passing). (`eb2e2e7`)
- **U2** — `PackerBaseline` (3 keys, Dispatcher-shape — K5) in RolePermissionsSeed + SeedAsync 4th loop + 10 unit facts + 2 integration scenarios (34-row provisioning AE1 + pack-confirm-on-Picker preservation AE9). (`e225d5b`)
- **U3** — `MarkPackFailed` endpoint + `PackFailed` saga event + `During(Picked, When(PackFailed))` Path D clause + `Order.MarkCompensatingReservation` allow-set widens to include `Picked` (K1) + 5 unit facts. (`980a9f0`)
- **U4** — HandoffFixture 4-role extension (BuildPackerJwt + PackerUserId) + 4-role HappyPath E2E + PackFailedE2ETests. (`e085d3e`)
- **U5** — CrossRoleDenialTests 6→14 facts (4 Packer baseline + 2 Picker/Dispatcher → mark-pack-failed + adversarial-F3 third pin + adversarial-F8 union-of-perms pin) + BuildPickerWithExtraPackConfirmJwt. (`0b17549`)
- **U6** — This sign-off + Auth AGENTS.md (Packer baseline + production-hardening) + adversarial-F3 solution note (third pin landed) + CHANGELOG + README + CLAUDE.md + annotated tag `v0.17.0-sprint-13`.

## 12 KTDs

1. **K1** — Order.Status is `Picked` (NOT `AwaitingPack`) when MarkPackFailed fires. Saga clause `During(Picked, When(PackFailed))`; allow-set widens to `Picked`. BLOCKING factual correction over the brainstorm (analogous to Sprint-12.5 R9); applied as safe_auto before code. Verified by feasibility reviewer against `Order.cs:241-257` + `OrdersController.ConfirmPackAsync` chaining.
2. **K2** — Single migration alters BOTH CHECK constraints. Confirmed both exist (Sprint-8 `chk_users_role` + Sprint-9 `chk_role_permissions_role`).
3. **K3** — MarkPackFailed reuses `outbound.orders.pack-confirm` (no 25th key). Sprint-12.5 KTD6 precedent.
4. **K4** — Path D reuses Path B/C compensation primitives unchanged. ReservedLineSkus + LinesAwaitingRelease survive through Picked.
5. **K5** — PackerBaseline = Dispatcher-shape (3 keys, no inventory.read).
6. **K6** — PackFailed positional-default `Guid? ActorUserId = null`.
7. **K7** — Owner KEEPS pack-confirm (ADDITIVE-ONLY KTD1).
8. **K8** — Extend HandoffFixture in-place; Sprint-12 happy-path preserved as regression coverage.
9. **K9** — Packer at enum index 3 (preserves Owner=0/Picker=1/Dispatcher=2 ordering).
10. **K10** — Migration timestamp `20260527000001`.
11. **K11** — Adversarial-F3 third pin + adversarial-F8 union-of-perms pin.
12. **K12** — Version bump `v0.17.0-sprint-13` (minor).

## Doc-review pipeline

5 reviewers (coherence + feasibility + security-lens + scope-guardian + adversarial) ran headless before code landed. **1 safe_auto fix applied** (SG-001: U5 missing 2 cross-role denial scenarios; plan counts corrected 6→14). The 7 coherence findings were false positives (the reviewer hallucinated the brainstorm's R-IDs as R1-R6 when it actually has R1-R15) and were suppressed. Feasibility confirmed all 11 plan claims (K1-K8 + file paths + migration timestamp + PendingModelChangesWarning ignore present).

## Deviations from plan

- **Pre-U0 csharpier cleanup commit added** — the husky pre-commit hook (functional after `dotnet husky install`) blocked on ~387 .cs files of pre-existing drift (Phase-0-redux-flagged). The plan did not anticipate the hook being broken; the cleanup commit (user-approved) is the "one cleanup commit" CLAUDE.md referenced.
- **No standalone `MarkPackFailedRequestTests.cs`** — the plan listed a DTO MaxLength test, but the sibling `MarkShipFailedRequest`/`MarkPickFailedRequest` have no standalone DTO tests (the attribute is framework behavior; AE6 is covered by the controller defence-in-depth guard + integration E2E). Matched the existing pattern.
- **Saga clause is single-state `During(Picked, ...)`** per K1, NOT the multi-state `During(Picked, AwaitingPack, ...)` the adversarial reviewer proposed (ADV-001/006). See carry-forward #1.

## Doc-review P1 findings carried forward (NOT applied — accepted as Sprint-13 trade-offs)

1. **ADV-001/006 — belt-and-braces multi-state `During(Picked, AwaitingPack, When(PackFailed))`.** The saga declares an `AwaitingPack` state it never enters; if a future sprint wires `Picked → AwaitingPack`, the single-state clause would silently miss those orders. Sprint-13 kept single-state per K1 (matches the actual state machine + Sprint-12.5 Path C symmetry). **If a future sprint adds the Picked → AwaitingPack auto-transition, add AwaitingPack to the PackFailed clause's state list.**
2. **ADV-002/008 — operator deploy runbook.** Deploying Sprint-13 against an existing tenant requires re-running `shopflow-migrate provision` after the migration (the migration widens the CHECK but inserts no Packer rows). Documented in Auth AGENTS.md Sprint-13 Packer baseline note.
3. **ADV-004 — saga PackFailed redelivery.** Covered behaviorally by `PackFailed_InWrongState_IsIgnoredAsOutOfBand` (once the saga moves past Picked, the clause no longer applies). An explicit `During(CompensatingReservation, Ignore(PackFailed))` was not added — MT's default out-of-band handling is the same as for PickFailed/ShipFailed, which also lack explicit Ignore clauses.
4. **SEC-004 — NULL-actor guard on MarkPackFailedAsync.** Not applied; would make MarkPackFailed inconsistent with its MarkPickFailed/MarkShipFailed siblings (all pass `IRequestContext.UserId` without a null guard). Cross-cutting hardening across all three endpoints is a separate workstream. Documented in Auth AGENTS.md production-hardening.

## Trade-offs carried forward to Sprint-13.5 / Sprint-14 / Phase-3

1. Frontend bits: `<ConfirmPackButton>`, `<MarkPackFailedModal>`, frontend MarkShipFailed button, Dispatcher UI views → Sprint-13.5 / Sprint-14.
2. Picker / Dispatcher / Packer MFA enforcement → production-hardening workstream.
3. Force-change-on-first-login enforcement → production-hardening.
4. `auth_audit_log` write-path on Outbound saga handlers (incl. new MarkPackFailedHandler) → still unwired; actor visibility via `actor_user_id` column.
5. SEC-004 NULL-actor guard (cross-cutting across all 3 mark-*-failed endpoints).
6. ADV-001 multi-state Path D clause (only if Picked → AwaitingPack auto-transition lands).
7. HandoffFixture / PickerFixture consolidation (Sprint-12 KTD4 deferred).
8. 3 candidate `/ce-compound` solution notes (UserRole CHECK widening pattern; HandoffFixture+NarrowedJwtBuilder Compile-link; ADDITIVE-ONLY contract).
9. Phase-3 polish: observability dashboards (per-role denial rates incl. Packer), `auth_audit_log` partitioning, KMS/Vault TOTP KEK migration, `CREATE INDEX CONCURRENTLY` review, PgBouncer pool re-validation.

## Next implementation step

Cut a fresh branch from `v0.17.0-sprint-13` and start either **Sprint-13.5** (frontend Packer/Dispatcher UI surfaces + MarkShipFailed button), **Sprint-13 hardening pre-work** (non-Owner MFA enforcement + force-change-on-first-login + Outbound audit-log wiring + SEC-004 NULL-actor guard), or **Phase-3 polish**.
