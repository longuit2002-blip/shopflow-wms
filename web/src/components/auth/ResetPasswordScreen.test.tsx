import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { ResetPasswordScreen } from './ResetPasswordScreen';
import { __resetLocaleForTests } from '../../hooks/useLocale';

describe('ResetPasswordScreen (Sprint-9.5 U6)', () => {
  beforeEach(() => {
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
  });
  afterEach(() => {
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  it('shows error panel when token is missing', () => {
    render(<ResetPasswordScreen token={null} />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /invalid|không hợp lệ/i })).toBeInTheDocument();
    expect(screen.queryByLabelText(/New password|Mật khẩu mới/i)).not.toBeInTheDocument();
  });

  it('shows error panel when token is empty string', () => {
    render(<ResetPasswordScreen token="" />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('Continue button on token-error fires onRequestNewLink', async () => {
    const user = userEvent.setup();
    const onRequestNewLink = vi.fn();
    render(<ResetPasswordScreen token={null} onRequestNewLink={onRequestNewLink} />);

    await user.click(screen.getByRole('button', { name: /Continue|Tiếp tục/i }));
    expect(onRequestNewLink).toHaveBeenCalledTimes(1);
  });

  it('submit calls /api/auth/reset-password with token + new password', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(new Response('{}', { status: 200 }));
    const onResetComplete = vi.fn();
    render(<ResetPasswordScreen token="reset-token-abc" onResetComplete={onResetComplete} />);

    await user.type(screen.getByLabelText(/New password|Mật khẩu mới/i), 'Strong!Pass123');
    await user.type(screen.getByLabelText(/Confirm password|Xác nhận/i), 'Strong!Pass123');
    await user.click(screen.getByRole('button', { name: /Reset password|Đặt lại mật khẩu/i }));

    await waitFor(() => expect(onResetComplete).toHaveBeenCalledTimes(1));
    const call = vi.mocked(globalThis.fetch).mock.calls[0]!;
    expect(call[0]).toBe('/api/auth/reset-password');
    const body = JSON.parse((call[1] as RequestInit).body as string);
    expect(body.token).toBe('reset-token-abc');
    expect(body.newPassword).toBe('Strong!Pass123');
  });

  it('inline error on 422 backend response', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          title: 'Invalid',
          detail: 'token expired',
          error_code: 'auth.reset_token_expired',
        }),
        { status: 422, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    const onResetComplete = vi.fn();
    render(<ResetPasswordScreen token="expired-token" onResetComplete={onResetComplete} />);

    await user.type(screen.getByLabelText(/New password|Mật khẩu mới/i), 'Strong!Pass123');
    await user.type(screen.getByLabelText(/Confirm password|Xác nhận/i), 'Strong!Pass123');
    await user.click(screen.getByRole('button', { name: /Reset password|Đặt lại mật khẩu/i }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/token expired/i);
    expect(onResetComplete).not.toHaveBeenCalled();
  });

  it('submit disabled until both fields meet the 12-char + match check', async () => {
    const user = userEvent.setup();
    render(<ResetPasswordScreen token="abc" />);

    const button = screen.getByRole('button', { name: /Reset password|Đặt lại mật khẩu/i });
    expect(button).toBeDisabled();

    await user.type(screen.getByLabelText(/New password|Mật khẩu mới/i), 'short');
    expect(button).toBeDisabled();

    await user.clear(screen.getByLabelText(/New password|Mật khẩu mới/i));
    await user.type(screen.getByLabelText(/New password|Mật khẩu mới/i), 'Strong!Pass123');
    await user.type(screen.getByLabelText(/Confirm password|Xác nhận/i), 'mismatch');
    expect(button).toBeDisabled();

    await user.clear(screen.getByLabelText(/Confirm password|Xác nhận/i));
    await user.type(screen.getByLabelText(/Confirm password|Xác nhận/i), 'Strong!Pass123');
    expect(button).toBeEnabled();
  });
});
