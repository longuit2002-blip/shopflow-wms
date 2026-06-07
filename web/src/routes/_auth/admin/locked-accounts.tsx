import { useEffect, useState } from 'react';
import { createFileRoute, redirect } from '@tanstack/react-router';
import { LockedAccountsTable } from '../../../components/admin/LockedAccountsTable';
import { t, useLocale } from '../../../hooks/useLocale';
import { useToast } from '../../../hooks/useToast';
import { hasPerm } from '../../../hooks/usePerm';
import { type AdminUser, listUsers, unlockAccount } from '../../../api/admin';

/**
 * Sprint-9.5 U7 — /admin/locked-accounts. Lists every account whose
 * lockedUntil is in the future. Guarded by
 * `usePerm('auth.admin.lockout.unlock')` via beforeLoad; users without
 * the permission redirect to /dashboard with a toast.
 */
export const Route = createFileRoute('/_auth/admin/locked-accounts')({
  beforeLoad: () => {
    if (!hasPerm('auth.admin.lockout.unlock')) {
      throw redirect({ to: '/dashboard' });
    }
  },
  component: LockedAccountsRoute,
});

function LockedAccountsRoute() {
  useLocale();
  const push = useToast((s) => s.push);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);

  async function reload() {
    setLoading(true);
    try {
      const list = await listUsers({ lockedOnly: true });
      const now = Date.now();
      // Defensive client-side filter in case the backend returns
      // historical lockouts: only currently-locked rows.
      setUsers(list.filter((u) => u.lockedUntil && new Date(u.lockedUntil).getTime() > now));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleUnlock(userId: string) {
    await unlockAccount(userId);
    push({ kind: 'success', title: t('Đã mở khóa', 'Account unlocked') });
    await reload();
  }

  return (
    <div style={{ padding: 'var(--s-6)', display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}>
      <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
        {t('Tài khoản bị khóa', 'Locked accounts')}
      </h1>
      {loading ? (
        <p role="status">{t('Đang tải…', 'Loading…')}</p>
      ) : (
        <LockedAccountsTable accounts={users} onUnlock={handleUnlock} />
      )}
    </div>
  );
}
