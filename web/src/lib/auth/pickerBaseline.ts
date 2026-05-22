/**
 * Sprint-11 U1 — frontend mirror of the canonical Picker 4-key
 * baseline pre-seeded into `role_permissions` at every tenant
 * provision (see `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs`
 * — `PickerBaseline` static field).
 *
 * Why a mirror exists: the backend list is the authoritative source
 * (it drives the actual seeded rows + JWT `perm[]` claims), but the
 * frontend needs the same strings literally for:
 *   - Sprint-9.5 U8 Sidebar test fixture (verifies a Picker session
 *     sees exactly the expected nav items),
 *   - RolePermissionsEditor "show baseline" annotations on the Picker
 *     column (Sprint-11+ surface),
 *   - Vitest fixtures that mint a fake Picker JWT.
 *
 * Both lists are the public contract — strings must stay in lock-step
 * between this file and {@link RolePermissionsSeed.PickerBaseline}. A
 * unit test (Vitest) that pins the four strings and a backend test
 * (xUnit) that pins the four constants together prevent silent drift.
 */
export const PICKER_BASELINE_PERMS: readonly string[] = [
  'outbound.orders.read',
  'outbound.orders.pick-confirm',
  'inventory.read',
  'hub.connect',
] as const;
