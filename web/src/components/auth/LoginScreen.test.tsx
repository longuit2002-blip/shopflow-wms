import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { LoginScreen } from './LoginScreen';
import { __resetAuthForTests, useAuth } from '../../hooks/useAuth';
import { __resetLocaleForTests } from '../../hooks/useLocale';

const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJvd25lckB5ZW5zYW8udm4iLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJ0ZW5hbnRfc2VsbGVyIiwidGVuYW50X3NsdWciOiJ5ZW5zYW9raGFuaGhvYSIsImV4cCI6OTk5OTk5OTk5OX0' +
  '.signature';

describe('LoginScreen', () => {
  beforeEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  it('renders Logo + email + password + 2FA placeholder + submit', () => {
    render(<LoginScreen />);

    expect(screen.getByRole('img', { name: /ShopFlow logo/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/Email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Mật khẩu/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Mã 2FA/i)).toBeDisabled();
    expect(screen.getByRole('button', { name: /Đăng nhập$/i })).toBeInTheDocument();
  });

  it('disables submit until both email and password are non-empty', async () => {
    const user = userEvent.setup();
    render(<LoginScreen />);
    const submit = screen.getByRole('button', { name: /Đăng nhập$/i });

    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText(/Email/i), 'owner@yensao.vn');
    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText(/Mật khẩu/i), 'x');
    expect(submit).toBeEnabled();
  });

  it('on submit, posts to /auth/login and calls onLoginSuccess', async () => {
    const user = userEvent.setup();
    const onLoginSuccess = vi.fn();

    vi.mocked(globalThis.fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          accessToken: VALID_JWT,
          expiresIn: 3600,
          tokenType: 'Bearer',
          user: {
            email: 'owner@yensao.vn',
            role: 'tenant_seller',
            tenantSlug: 'yensaokhanhhoa',
          },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    render(<LoginScreen onLoginSuccess={onLoginSuccess} />);
    await user.type(screen.getByLabelText(/Email/i), 'owner@yensao.vn');
    await user.type(screen.getByLabelText(/Mật khẩu/i), 'any-password');
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    await waitFor(() => expect(onLoginSuccess).toHaveBeenCalled());

    const [url, init] = vi.mocked(globalThis.fetch).mock.calls[0]!;
    expect(url).toBe('/auth/login');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({
      email: 'owner@yensao.vn',
      password: 'any-password',
    });

    expect(useAuth.getState().isAuthenticated).toBe(true);
    expect(useAuth.getState().user?.tenantSlug).toBe('yensaokhanhhoa');
  });

  it('on 401, shows the error alert', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(
      new Response(JSON.stringify({ title: 'Invalid credentials' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    render(<LoginScreen />);
    await user.type(screen.getByLabelText(/Email/i), 'x@y.vn');
    await user.type(screen.getByLabelText(/Mật khẩu/i), 'wrong');
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument();
    });
    expect(useAuth.getState().isAuthenticated).toBe(false);
  });
});
