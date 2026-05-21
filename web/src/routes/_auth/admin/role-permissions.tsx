import { useEffect, useState } from 'react';
import { createFileRoute, redirect } from '@tanstack/react-router';
import { RolePermissionsEditor } from '../../../components/admin/RolePermissionsEditor';
import { t, useLocale } from '../../../hooks/useLocale';
import { useToast } from '../../../hooks/useToast';
import { hasPerm, usePerm } from '../../../hooks/usePerm';
import {
  type EditableRole,
  type RolePermissions,
  getRolePermissions,
  updateRolePermissions,
} from '../../../api/admin';

/**
 * Sprint-9.5 U7 — /admin/role-permissions. Read access requires
 * `auth.admin.role-permissions.read`; write access requires
 * `auth.admin.role-permissions.update` (RolePermissionsEditor disables
 * checkboxes when canEdit is false).
 */
export const Route = createFileRoute('/_auth/admin/role-permissions')({
  beforeLoad: () => {
    if (!hasPerm('auth.admin.role-permissions.read')) {
      throw redirect({ to: '/dashboard' });
    }
  },
  component: RolePermissionsRoute,
});

function RolePermissionsRoute() {
  useLocale();
  const canEdit = usePerm('auth.admin.role-permissions.update');
  const push = useToast((s) => s.push);

  const [data, setData] = useState<RolePermissions | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function reload() {
    try {
      const result = await getRolePermissions();
      setData(result);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  useEffect(() => {
    reload();
  }, []);

  async function handleSave(role: EditableRole, keys: readonly string[]) {
    await updateRolePermissions({ role, permissionKeys: keys });
    push({
      kind: 'success',
      title: t('Đã cập nhật quyền', 'Permissions updated'),
      body: t(
        'Thay đổi sẽ lan ra trong vòng 15 phút.',
        'Changes propagate within 15 minutes.',
      ),
    });
    await reload();
  }

  if (error) {
    return (
      <div style={{ padding: 'var(--s-6)' }}>
        <p role="alert">{error}</p>
      </div>
    );
  }
  if (!data) {
    return (
      <div style={{ padding: 'var(--s-6)' }}>
        <p role="status">{t('Đang tải…', 'Loading…')}</p>
      </div>
    );
  }
  return (
    <div style={{ padding: 'var(--s-6)' }}>
      <RolePermissionsEditor initial={data} canEdit={canEdit} onSave={handleSave} />
    </div>
  );
}
