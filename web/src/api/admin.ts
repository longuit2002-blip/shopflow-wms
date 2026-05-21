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
 * Sprint-9 permission catalog. Mirrors PermissionKeys.All from
 * SharedKernel. Module grouping drives the RolePermissionsEditor's
 * collapsible sections.
 */
export const PERMISSION_KEYS: readonly { key: string; module: string }[] = [
  // Auth admin
  { key: 'auth.admin.users.list', module: 'Auth' },
  { key: 'auth.admin.users.create', module: 'Auth' },
  { key: 'auth.admin.users.update', module: 'Auth' },
  { key: 'auth.admin.users.deactivate', module: 'Auth' },
  { key: 'auth.admin.lockout.unlock', module: 'Auth' },
  { key: 'auth.admin.mfa-reset', module: 'Auth' },
  { key: 'auth.admin.role-permissions.read', module: 'Auth' },
  { key: 'auth.admin.role-permissions.update', module: 'Auth' },
  // Inventory
  { key: 'inventory.read', module: 'Inventory' },
  { key: 'inventory.adjust', module: 'Inventory' },
  { key: 'inventory.flash-sale.toggle', module: 'Inventory' },
  { key: 'inventory.skus.create', module: 'Inventory' },
  { key: 'inventory.skus.update', module: 'Inventory' },
  // Outbound / Orders
  { key: 'outbound.orders.read', module: 'Outbound' },
  { key: 'outbound.orders.confirm-pick', module: 'Outbound' },
  { key: 'outbound.orders.confirm-pack', module: 'Outbound' },
  { key: 'outbound.orders.confirm-ship', module: 'Outbound' },
  { key: 'outbound.orders.mark-pick-failed', module: 'Outbound' },
  // Inbound
  { key: 'inbound.pos.read', module: 'Inbound' },
  { key: 'inbound.pos.create', module: 'Inbound' },
  { key: 'inbound.receiving.confirm', module: 'Inbound' },
  // Hub (SignalR)
  { key: 'hub.tenant.read', module: 'Hub' },
  { key: 'hub.tenant.write', module: 'Hub' },
  // Notifications
  { key: 'notification.dlq.read', module: 'Notification' },
];

/** Sprint-9 KTD13 OwnerCritical guard — client mirror. */
export const OWNER_CRITICAL_KEYS: readonly string[] = [
  'auth.admin.users.list',
  'auth.admin.users.create',
  'auth.admin.users.update',
  'auth.admin.users.deactivate',
  'auth.admin.lockout.unlock',
  'auth.admin.mfa-reset',
  'auth.admin.role-permissions.read',
  'auth.admin.role-permissions.update',
  'inventory.read',
];

export const MODULES: readonly string[] = ['Auth', 'Inventory', 'Outbound', 'Inbound', 'Hub', 'Notification'];
