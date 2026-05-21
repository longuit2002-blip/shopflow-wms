import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { ProfileSecurityScreen } from './ProfileSecurityScreen';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';
import { __resetLocaleForTests } from '../../hooks/useLocale';

const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

function freshSession() {
  return {
    accessToken: VALID_JWT,
    refreshToken: 'opaque',
    accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

describe('ProfileSecurityScreen (Sprint-9.5 U6)', () => {
  beforeEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
    URL.createObjectURL = vi.fn(() => 'blob:fake');
    URL.revokeObjectURL = vi.fn();
    useAuth.getState().setSession(freshSession());
  });
  afterEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  it('shows Enable MFA when not enrolled', () => {
    render(<ProfileSecurityScreen mfaEnrolled={false} />);
    expect(screen.getByRole('button', { name: /Enable MFA|Bật MFA/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Disable MFA|Tắt MFA/i })).not.toBeInTheDocument();
  });

  it('shows Disable MFA when enrolled', () => {
    render(<ProfileSecurityScreen mfaEnrolled={true} />);
    expect(screen.getByRole('button', { name: /Disable MFA|Tắt MFA/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Enable MFA|Bật MFA/i })).not.toBeInTheDocument();
  });

  it('Disable MFA opens a confirmation modal requiring password', async () => {
    const user = userEvent.setup();
    render(<ProfileSecurityScreen mfaEnrolled={true} />);
    await user.click(screen.getByRole('button', { name: /Disable MFA|Tắt MFA/i }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
    // Scope the password lookup to the modal so it doesn't collide with
    // the change-password card's "Current password" field on the page.
    expect(
      within(dialog).getByLabelText(/Current password|Mật khẩu hiện tại/i),
    ).toBeInTheDocument();
  });

  it('Regenerate codes button renders RecoveryCodesDisplay on success', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          recoveryCodes: [
            'aaaa-1111', 'bbbb-2222', 'cccc-3333', 'dddd-4444', 'eeee-5555',
            'ffff-6666', 'gggg-7777', 'hhhh-8888', 'iiii-9999', 'jjjj-0000',
          ],
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    render(<ProfileSecurityScreen mfaEnrolled={true} />);
    await user.click(screen.getByRole('button', { name: /Regenerate codes|Tạo mã mới/i }));

    await waitFor(() => {
      expect(screen.getByTestId('recovery-codes-display')).toBeInTheDocument();
    });
    expect(screen.getByText('aaaa-1111')).toBeInTheDocument();
  });

  it('Change password validates min 12 chars + matching confirm', async () => {
    const user = userEvent.setup();
    render(<ProfileSecurityScreen mfaEnrolled={true} />);

    const button = screen.getByRole('button', { name: /Change password|Đổi mật khẩu/i });
    expect(button).toBeDisabled();

    await user.type(screen.getByLabelText(/Current password|Mật khẩu hiện tại/i), 'oldPass');
    await user.type(screen.getByLabelText(/New password|Mật khẩu mới/i), 'short');
    await user.type(screen.getByLabelText(/Confirm|Xác nhận/i), 'short');
    expect(button).toBeDisabled();

    await user.clear(screen.getByLabelText(/New password|Mật khẩu mới/i));
    await user.clear(screen.getByLabelText(/Confirm|Xác nhận/i));
    await user.type(screen.getByLabelText(/New password|Mật khẩu mới/i), 'StrongPass!1234');
    await user.type(screen.getByLabelText(/Confirm|Xác nhận/i), 'mismatch');
    expect(button).toBeDisabled();

    await user.clear(screen.getByLabelText(/Confirm|Xác nhận/i));
    await user.type(screen.getByLabelText(/Confirm|Xác nhận/i), 'StrongPass!1234');
    expect(button).toBeEnabled();
  });
});
