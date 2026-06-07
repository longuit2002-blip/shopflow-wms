/**
 * Sprint-12 U2 — frontend mirror of the canonical Dispatcher 3-key
 * baseline pre-seeded into `role_permissions` at every tenant
 * provision (see `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs`
 * — `DispatcherBaseline` static field).
 *
 * Dispatcher owns the ship-confirm transition (Owner pack-confirms;
 * Picker pick-confirms; Dispatcher ship-confirms). The Sprint-12 plan
 * intentionally does NOT include `outbound.orders.pack-confirm` — Pack
 * stays Owner-only at Sprint-12 (no Packer fourth role).
 *
 * Why a mirror exists: the backend list is the authoritative source
 * (it drives the actual seeded rows + JWT `perm[]` claims), but the
 * frontend needs the same strings literally for:
 *   - Vitest fixtures that mint a fake Dispatcher JWT to assert the
 *     ConfirmShip button visibility on order-detail,
 *   - RolePermissionsEditor "show baseline" annotations on the
 *     Dispatcher column (Sprint-9.5 U7 surface).
 *
 * Both lists are the public contract — strings must stay in lock-step
 * between this file and {@link RolePermissionsSeed.DispatcherBaseline}.
 * Per-component coverage at U3 catches drift quickly (Sprint-12 KTD9
 * deferred a dedicated reflection contract test in favor of this
 * lower-cost coverage).
 */
export const DISPATCHER_BASELINE_PERMS: readonly string[] = [
  'outbound.orders.read',
  'outbound.orders.ship-confirm',
  'hub.connect',
] as const;
