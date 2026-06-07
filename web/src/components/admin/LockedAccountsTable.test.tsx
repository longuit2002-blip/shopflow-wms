import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { LockedAccountsTable } from './LockedAccountsTable';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import type { AdminUser } from '../../api/admin';

function makeAccount(overrides: Partial<AdminUser> = {}): AdminUser {
  const lockedUntil = new Date(Date.now() + 5 * 60 * 1000).toISOString();
  return {
    userId: overrides.userId ?? 'user-1',
    email: overrides.email ?? 'locked1@example.com',
    role: overrides.role ?? 'Picker',
    isActive: true,
    mfaEnrolled: false,
    mfaRequired: false,
    lockedUntil: overrides.lockedUntil ?? lockedUntil,
    failedLoginCount: 5,
    lastLoginAt: null,
    createdAt: new Date().toISOString(),
    ...overrides,
  };
}

describe('LockedAccountsTable (Sprint-9.5 U7)', () => {
  beforeEach(() => __resetLocaleForTests());
  afterEach(() => __resetLocaleForTests());

  it('shows empty state when no accounts are locked', () => {
    render(<LockedAccountsTable accounts={[]} onUnlock={vi.fn()} />);
    expect(screen.getByRole('status').textContent).toMatch(
      /No accounts are currently locked|Không có tài khoản/i,
    );
  });

  it('renders one row per locked account', () => {
    render(
      <LockedAccountsTable
        accounts={[
          makeAccount({ userId: 'u1', email: 'a@x.com' }),
          makeAccount({ userId: 'u2', email: 'b@y.com' }),
        ]}
        onUnlock={vi.fn()}
      />,
    );
    expect(screen.getByText('a@x.com')).toBeInTheDocument();
    expect(screen.getByText('b@y.com')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Unlock|Mở khóa/i })).toHaveLength(2);
  });

  it('clicking Unlock fires onUnlock with the right userId', async () => {
    const user = userEvent.setup();
    const onUnlock = vi.fn().mockResolvedValue(undefined);
    render(
      <LockedAccountsTable
        accounts={[makeAccount({ userId: 'user-x' })]}
        onUnlock={onUnlock}
      />,
    );

    await user.click(screen.getByRole('button', { name: /Unlock|Mở khóa/i }));
    await waitFor(() => expect(onUnlock).toHaveBeenCalledWith('user-x'));
  });

  it('renders the "Locked until" timestamp + a remaining cell', () => {
    const lockedUntil = new Date(Date.now() + 3 * 60 * 1000).toISOString();
    render(
      <LockedAccountsTable
        accounts={[makeAccount({ lockedUntil })]}
        onUnlock={vi.fn()}
      />,
    );
    expect(screen.getByText(lockedUntil)).toBeInTheDocument();
    // Remaining cell shows m:ss format roughly under 5 min.
    const remainingCells = screen.getAllByText(/^\d+:\d{2}$/);
    expect(remainingCells.length).toBeGreaterThan(0);
  });
});
