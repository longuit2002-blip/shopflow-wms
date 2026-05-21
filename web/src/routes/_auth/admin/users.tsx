import { useEffect, useState } from 'react';
import { createFileRoute, redirect } from '@tanstack/react-router';
import { Button } from '../../../components/primitives/Button';
import { MfaStatusBadge, deriveMfaStatus } from '../../../components/admin/MfaStatusBadge';
import { t, useLocale } from '../../../hooks/useLocale';
import { useToast } from '../../../hooks/useToast';
import { usePerm, hasPerm } from '../../../hooks/usePerm';
import {
  type AdminUser,
  listUsers,
  adminMfaReset,
  unlockAccount,
} from '../../../api/admin';

/**
 * Sprint-9.5 U7 — Owner-only /admin/users route. Lists every user in
 * the tenant with the MFA status column + per-row Reset MFA + Unlock
 * actions. Route guarded by `usePerm('auth.admin.users.list')`.
 */
export const Route = createFileRoute('/_auth/admin/users')({
  beforeLoad: () => {
    if (!hasPerm('auth.admin.users.list')) {
      throw redirect({ to: '/dashboard' });
    }
  },
  component: AdminUsersRoute,
});

function AdminUsersRoute() {
  useLocale();
  const canResetMfa = usePerm('auth.admin.mfa-reset');
  const canUnlock = usePerm('auth.admin.lockout.unlock');
  const push = useToast((s) => s.push);

  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [confirm, setConfirm] = useState<
    | null
    | { kind: 'mfa-reset'; user: AdminUser }
    | { kind: 'unlock'; user: AdminUser }
  >(null);
  const [submitting, setSubmitting] = useState(false);

  async function reload() {
    setLoading(true);
    try {
      const list = await listUsers();
      setUsers(list);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleConfirm() {
    if (!confirm) return;
    setSubmitting(true);
    try {
      if (confirm.kind === 'mfa-reset') {
        await adminMfaReset(confirm.user.userId);
        push({ kind: 'success', title: t('Đã đặt lại MFA', 'MFA reset') });
      } else {
        await unlockAccount(confirm.user.userId);
        push({ kind: 'success', title: t('Đã mở khóa', 'Account unlocked') });
      }
      setConfirm(null);
      await reload();
    } catch (err) {
      push({
        kind: 'error',
        title: t('Lỗi', 'Error'),
        body: err instanceof Error ? err.message : String(err),
      });
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <div style={{ padding: 'var(--s-6)' }}>
        <p role="status">{t('Đang tải…', 'Loading…')}</p>
      </div>
    );
  }

  const now = Date.now();

  return (
    <div style={{ padding: 'var(--s-6)', display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}>
      <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
        {t('Người dùng', 'Users')}
      </h1>

      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th scope="col" style={th}>{t('Email', 'Email')}</th>
            <th scope="col" style={th}>{t('Vai trò', 'Role')}</th>
            <th scope="col" style={th}>MFA</th>
            <th scope="col" style={th}>{t('Khóa', 'Locked')}</th>
            <th scope="col" style={th}>
              <span className="sr-only">{t('Hành động', 'Actions')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {users.map((u) => {
            const status = deriveMfaStatus({
              mfaEnrolled: u.mfaEnrolled,
              mfaRequired: u.mfaRequired,
            });
            const isLocked = u.lockedUntil && new Date(u.lockedUntil).getTime() > now;
            return (
              <tr key={u.userId}>
                <td style={td}>{u.email}</td>
                <td style={td}>{u.role}</td>
                <td style={td}><MfaStatusBadge status={status} /></td>
                <td style={td}>{isLocked ? '🔒' : ''}</td>
                <td style={td}>
                  <div style={{ display: 'flex', gap: 'var(--s-2)' }}>
                    {canResetMfa && (
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={() => setConfirm({ kind: 'mfa-reset', user: u })}
                      >
                        {t('Đặt lại MFA', 'Reset MFA')}
                      </Button>
                    )}
                    {canUnlock && isLocked && (
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={() => setConfirm({ kind: 'unlock', user: u })}
                      >
                        {t('Mở khóa', 'Unlock')}
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {confirm && (
        <div
          role="dialog"
          aria-modal="true"
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(0,0,0,0.4)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 100,
          }}
        >
          <div className="card" style={{ width: 420, padding: 'var(--s-6)', display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}>
            <h2 className="t-lg" style={{ margin: 0 }}>
              {confirm.kind === 'mfa-reset'
                ? t('Đặt lại MFA?', 'Reset MFA?')
                : t('Mở khóa tài khoản?', 'Unlock account?')}
            </h2>
            <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
              {confirm.kind === 'mfa-reset'
                ? t(
                    `Đặt lại MFA cho ${confirm.user.email}? Họ sẽ cần đăng ký lại khi đăng nhập tiếp theo.`,
                    `Reset MFA for ${confirm.user.email}? They will need to re-enroll on next login.`,
                  )
                : t(
                    `Mở khóa ${confirm.user.email}?`,
                    `Unlock ${confirm.user.email}?`,
                  )}
            </p>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--s-2)' }}>
              <Button type="button" variant="secondary" size="md" disabled={submitting} onClick={() => setConfirm(null)}>
                {t('Hủy', 'Cancel')}
              </Button>
              <Button type="button" variant="primary" size="md" disabled={submitting} onClick={handleConfirm}>
                {submitting ? t('Đang xử lý…', 'Working…') : t('Xác nhận', 'Confirm')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

const th = {
  padding: 'var(--s-3) var(--s-4)',
  textAlign: 'left' as const,
  borderBottom: '1px solid var(--border)',
  fontWeight: 500,
  color: 'var(--ink-2)',
  fontSize: 13,
};
const td = {
  padding: 'var(--s-3) var(--s-4)',
  borderBottom: '1px solid var(--border-soft)',
};
