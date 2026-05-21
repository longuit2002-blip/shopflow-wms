import { useEffect, useState } from 'react';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import type { AdminUser } from '../../api/admin';

export interface LockedAccountsTableProps {
  accounts: readonly AdminUser[];
  onUnlock: (userId: string) => Promise<void> | void;
  /** For tests — inject a clock instead of relying on real Date.now ticks. */
  nowMs?: number;
}

/**
 * Sprint-9.5 U7 — `/admin/locked-accounts` table. Auto-ticks the
 * "remaining" cell once per second (jsdom timers in tests; real
 * setInterval in production). Empty state when no accounts.
 */
export function LockedAccountsTable({ accounts, onUnlock }: LockedAccountsTableProps) {
  useLocale();
  const [now, setNow] = useState(() => Date.now());
  const [pendingUserId, setPendingUserId] = useState<string | null>(null);

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  if (accounts.length === 0) {
    return (
      <div
        role="status"
        style={{
          padding: 'var(--s-6)',
          textAlign: 'center',
          color: 'var(--ink-2)',
        }}
      >
        {t('Không có tài khoản nào bị khóa.', 'No accounts are currently locked.')}
      </div>
    );
  }

  async function handleClick(userId: string) {
    setPendingUserId(userId);
    try {
      await onUnlock(userId);
    } finally {
      setPendingUserId(null);
    }
  }

  return (
    <table
      aria-label={t('Tài khoản bị khóa', 'Locked accounts')}
      style={{ width: '100%', borderCollapse: 'collapse' }}
    >
      <thead>
        <tr>
          <th scope="col" style={th}>
            {t('Email', 'Email')}
          </th>
          <th scope="col" style={th}>
            {t('Vai trò', 'Role')}
          </th>
          <th scope="col" style={th}>
            {t('Đến', 'Locked until')}
          </th>
          <th scope="col" style={th}>
            {t('Còn lại', 'Remaining')}
          </th>
          <th scope="col" style={th}>
            <span className="sr-only">{t('Hành động', 'Action')}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {accounts.map((a) => {
          const lockedUntilMs = a.lockedUntil ? new Date(a.lockedUntil).getTime() : 0;
          const remainingSec = Math.max(0, Math.floor((lockedUntilMs - now) / 1000));
          return (
            <tr key={a.userId}>
              <td style={td}>{a.email}</td>
              <td style={td}>{a.role}</td>
              <td style={td}>{a.lockedUntil}</td>
              <td style={td} aria-live="polite">
                {formatRemaining(remainingSec)}
              </td>
              <td style={td}>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  disabled={pendingUserId === a.userId}
                  onClick={() => handleClick(a.userId)}
                >
                  {pendingUserId === a.userId
                    ? t('Đang mở khóa…', 'Unlocking…')
                    : t('Mở khóa', 'Unlock')}
                </Button>
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

function formatRemaining(seconds: number): string {
  if (seconds <= 0) return '0:00';
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
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
