/**
 * Sprint-9.5 U7 — admin surface API wrappers around the Sprint-9 backend
 * admin endpoints (Owner-gated by `[Authorize(Roles="Owner")]`).
 *
 * All calls go through httpClient so they carry the access token + tenant
 * header + idempotency key per Sprint-8 contract. 403 falls through as
 * ApiError with the problem-details errorCode propagated.
 */

import { httpClient } from './httpClient';

export interface AdminUser {
  userId: string;
  email: string;
  role: string;
  isActive: boolean;
  mfaEnrolled: boolean;
  mfaRequired: boolean;
  lockedUntil: string | null;
  failedLoginCount: number;
  lastLoginAt: string | null;
  createdAt: string;
}

export interface ListUsersResponse {
  users: AdminUser[];
}

export async function listUsers(opts: { lockedOnly?: boolean } = {}): Promise<AdminUser[]> {
  const qs = opts.lockedOnly ? '?lockedOnly=true' : '';
  const result = await httpClient.get<ListUsersResponse | AdminUser[]>(
    `/api/auth/admin/users${qs}`,
  );
  // Backend may return either shape — accept both for resilience.
  if (Array.isArray(result)) return result;
  return result?.users ?? [];
}

export async function adminMfaReset(userId: string): Promise<void> {
  await httpClient.post<void>('/api/auth/admin/mfa-reset', { userId });
}

export async function unlockAccount(userId: string): Promise<void> {
  await httpClient.post<void>('/api/auth/admin/unlock', { userId });
}

/** Backend shape: { Owner: string[], Picker: string[], Dispatcher: string[] }. */
export interface RolePermissions {
  Owner: string[];
  Picker: string[];
  Dispatcher: string[];
}

export async function getRolePermissions(): Promise<RolePermissions> {
  const result = await httpClient.get<RolePermissions>('/api/auth/admin/role-permissions');
  return {
    Owner: result.Owner ?? [],
    Picker: result.Picker ?? [],
    Dispatcher: result.Dispatcher ?? [],
  };
}

export type EditableRole = 'Picker' | 'Dispatcher';

export interface UpdateRolePermissionsRequest {
  role: EditableRole;
  permissionKeys: readonly string[];
}

export async function updateRolePermissions(
  req: UpdateRolePermissionsRequest,
): Promise<void> {
  await httpClient.put<void>('/api/auth/admin/role-permissions', req);
}

/**
 * Sprint-10.5 U1 — permission catalog rewrite. Exact-mirrors the 24
 * backend keys in `ShopFlow.SharedKernel.Authorization.PermissionKeys`
 * (Sprint-9 U1). Module grouping drives the RolePermissionsEditor's
 * collapsible sections.
 *
 * The Sprint-9.5 U7 version had drifted ~9 entries (used `.update` /
 * `.create` / `.toggle` / `.confirm-*` / `notification.dlq.*` /
 * `hub.tenant.*` names that don't exist server-side). Sprint-10 attached
 * policies to 33 actions using the canonical names, making the drift a
 * privilege-escalation vector once Sprint-11's non-Owner role lands.
 */
export const PERMISSION_KEYS: readonly { key: string; module: string }[] = [
  // Auth admin (9)
  { key: 'auth.admin.users.list', module: 'Auth' },
  { key: 'auth.admin.users.create', module: 'Auth' },
  { key: 'auth.admin.users.update-role', module: 'Auth' },
  { key: 'auth.admin.users.reset-password', module: 'Auth' },
  { key: 'auth.admin.users.deactivate', module: 'Auth' },
  { key: 'auth.admin.lockout.unlock', module: 'Auth' },
  { key: 'auth.admin.mfa-reset', module: 'Auth' },
  { key: 'auth.admin.role-permissions.read', module: 'Auth' },
  { key: 'auth.admin.role-permissions.update', module: 'Auth' },
  // Inventory (5)
  { key: 'inventory.read', module: 'Inventory' },
  { key: 'inventory.adjust', module: 'Inventory' },
  { key: 'inventory.skus.write', module: 'Inventory' },
  { key: 'inventory.skus.flash-sale.write', module: 'Inventory' },
  { key: 'inventory.skus.threshold.write', module: 'Inventory' },
  // Outbound / Orders (6)
  { key: 'outbound.orders.read', module: 'Outbound' },
  { key: 'outbound.orders.write', module: 'Outbound' },
  { key: 'outbound.orders.pick-confirm', module: 'Outbound' },
  { key: 'outbound.orders.pack-confirm', module: 'Outbound' },
  { key: 'outbound.orders.ship-confirm', module: 'Outbound' },
  { key: 'outbound.orders.cancel', module: 'Outbound' },
  // Inbound (3)
  { key: 'inbound.pos.read', module: 'Inbound' },
  { key: 'inbound.pos.write', module: 'Inbound' },
  { key: 'inbound.receive.confirm', module: 'Inbound' },
  // Hub (SignalR) (1)
  { key: 'hub.connect', module: 'Hub' },
];

/**
 * Sprint-9 KTD13 OwnerCritical guard — client mirror. Exact-mirrors
 * `PermissionKeys.OwnerCritical` (9 entries, all `auth.admin.*`).
 * Server-side guard in `RolePermissionsCommandHandler` remains
 * authoritative; this list drives UX cues only.
 */
export const OWNER_CRITICAL_KEYS: readonly string[] = [
  'auth.admin.users.list',
  'auth.admin.users.create',
  'auth.admin.users.update-role',
  'auth.admin.users.reset-password',
  'auth.admin.users.deactivate',
  'auth.admin.lockout.unlock',
  'auth.admin.mfa-reset',
  'auth.admin.role-permissions.read',
  'auth.admin.role-permissions.update',
];

/**
 * Sprint-10.5 U1 — permission keys catalogued server-side but not yet
 * attached to any controller action. Granting these to Picker /
 * Dispatcher is a no-op today but would grant any future action
 * attached. The RolePermissionsEditor disables the Picker + Dispatcher
 * toggles for these rows + surfaces an `aria-describedby` tooltip so an
 * Owner can't silently arm a future privilege (SEC-006 / adv-1 risk).
 *
 * `hub.connect` is NOT orphan post-Sprint-10.5 U3 — that unit attaches
 * the policy to the SignalR connect handshake.
 */
export const ORPHAN_KEYS: readonly string[] = ['outbound.orders.cancel'];

export const MODULES: readonly string[] = ['Auth', 'Inventory', 'Outbound', 'Inbound', 'Hub'];
