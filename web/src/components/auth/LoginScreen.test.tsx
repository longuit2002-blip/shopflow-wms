import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { LoginScreen } from './LoginScreen';
import { __resetAuthForTests, useAuth } from '../../hooks/useAuth';
import { __resetLocaleForTests } from '../../hooks/useLocale';

/**
 * Sprint-8.5 U3 — restored under the Sprint-8 U11 LoginScreen contract.
 *
 * The Sprint-6 version of this file (deleted at commit 47412ec) tested
 * POST /auth/login with {email, password} only. Sprint-8 U11 lifted to:
 *
 *   - POST /api/auth/login with {email, password, rememberMe, tenantSlug}
 *   - Workspace (tenant_slug) field with subdomain auto-detect
 *     (read-only when hostname has non-reserved subdomain, editable on
 *     localhost / IP literals / reserved slugs)
 *   - RememberMe checkbox
 *   - useAuth.setSession(StoredSession) instead of useAuth.login(jwt)
 *   - LoginResponse carries {accessToken, accessTokenExpiresAt,
 *     refreshToken, refreshTokenExpiresAt, role, email}
 *
 * Test scaffolding: Object.defineProperty(window, 'location', ...) stubs
 * the hostname per test scenario. detectTenantFromHost is a pure
 * function — testing it via the LoginScreen integration is enough; no
 * direct unit-test needed.
 */

const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

const ACCESS_EXPIRES = new Date(Date.now() + 15 * 60 * 1000).toISOString();
const REFRESH_EXPIRES = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();

function loginOkResponse() {
  return new Response(
    JSON.stringify({
      accessToken: VALID_JWT,
      accessTokenExpiresAt: ACCESS_EXPIRES,
      refreshToken: 'opaque-refresh-1',
      refreshTokenExpiresAt: REFRESH_EXPIRES,
      role: 'Owner',
      email: 'owner@yensao.vn',
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

// jsdom's window.location is non-configurable; stub the global with a
// minimal Location-shaped object carrying the fields LoginScreen reads.
function stubHostname(host: string) {
  vi.stubGlobal('location', {
    hostname: host,
    href: `http://${host}/`,
    origin: `http://${host}`,
    protocol: 'http:',
    host,
    pathname: '/',
    search: '',
    hash: '',
    assign: vi.fn(),
    replace: vi.fn(),
    reload: vi.fn(),
  });
}

describe('LoginScreen — Sprint-8 token-pair + tenant_slug + rememberMe', () => {
  beforeEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
    // Default to localhost so the user has to fill tenant manually
    // unless a specific test overrides.
    stubHostname('localhost');
  });

  afterEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  // ───────────── Element shape ─────────────

  it('renders email + password + tenant + rememberMe + submit', () => {
    render(<LoginScreen />);

    expect(screen.getByLabelText(/Email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Mật khẩu/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Workspace/i)).toBeInTheDocument();
    // RememberMe is checkbox without a strict label association; find by role.
    expect(screen.getByRole('checkbox')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đăng nhập$/i })).toBeInTheDocument();
  });

  it('submit disabled until email + password + tenant are non-empty', async () => {
    const user = userEvent.setup();
    render(<LoginScreen />);
    const submit = screen.getByRole('button', { name: /Đăng nhập$/i });

    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText(/Email/i), 'owner@yensao.vn');
    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText(/Mật khẩu/i), 'x');
    // tenant still empty → still disabled
    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText(/Workspace/i), 'yensaokhanhhoa');
    expect(submit).toBeEnabled();
  });

  // ───────────── detectTenantFromHost integration ─────────────

  it('hostname yensaokhanhhoa.shopflow.com → tenant prefilled + read-only', () => {
    stubHostname('yensaokhanhhoa.shopflow.com');
    render(<LoginScreen />);

    const tenant = screen.getByLabelText(/Workspace/i) as HTMLInputElement;
    expect(tenant.value).toBe('yensaokhanhhoa');
    expect(tenant.readOnly).toBe(true);
  });

  it('hostname localhost → tenant empty + editable', () => {
    stubHostname('localhost');
    render(<LoginScreen />);

    const tenant = screen.getByLabelText(/Workspace/i) as HTMLInputElement;
    expect(tenant.value).toBe('');
    expect(tenant.readOnly).toBe(false);
  });

  it('hostname 127.0.0.1 → tenant empty + editable', () => {
    stubHostname('127.0.0.1');
    render(<LoginScreen />);

    const tenant = screen.getByLabelText(/Workspace/i) as HTMLInputElement;
    expect(tenant.value).toBe('');
    expect(tenant.readOnly).toBe(false);
  });

  it('hostname api.shopflow.com (reserved slug) → tenant empty + editable', () => {
    stubHostname('api.shopflow.com');
    render(<LoginScreen />);

    const tenant = screen.getByLabelText(/Workspace/i) as HTMLInputElement;
    expect(tenant.value).toBe('');
    expect(tenant.readOnly).toBe(false);
  });

  // ───────────── RememberMe + submit + setSession ─────────────

  it('RememberMe starts unchecked; clicking flips it', async () => {
    const user = userEvent.setup();
    render(<LoginScreen />);

    const remember = screen.getByRole('checkbox') as HTMLInputElement;
    expect(remember.checked).toBe(false);

    await user.click(remember);
    expect(remember.checked).toBe(true);
  });

  it('submit with rememberMe=true → POST /api/auth/login carries the flag', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(loginOkResponse());

    render(<LoginScreen />);
    await user.type(screen.getByLabelText(/Email/i), 'owner@yensao.vn');
    await user.type(screen.getByLabelText(/Mật khẩu/i), 'any-password');
    await user.type(screen.getByLabelText(/Workspace/i), 'yensaokhanhhoa');
    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    await waitFor(() => {
      expect(vi.mocked(globalThis.fetch)).toHaveBeenCalled();
    });

    const [url, init] = vi.mocked(globalThis.fetch).mock.calls[0]!;
    expect(url).toBe('/api/auth/login');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({
      email: 'owner@yensao.vn',
      password: 'any-password',
      rememberMe: true,
      tenantSlug: 'yensaokhanhhoa',
    });
  });

  it('submit with rememberMe=false → POST carries false', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(loginOkResponse());

    render(<LoginScreen />);
    await user.type(screen.getByLabelText(/Email/i), 'owner@yensao.vn');
    await user.type(screen.getByLabelText(/Mật khẩu/i), 'any-password');
    await user.type(screen.getByLabelText(/Workspace/i), 'yensaokhanhhoa');
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    await waitFor(() => {
      expect(vi.mocked(globalThis.fetch)).toHaveBeenCalled();
    });

    const init = vi.mocked(globalThis.fetch).mock.calls[0]![1] as RequestInit;
    expect(JSON.parse(init.body as string)).toMatchObject({
      rememberMe: false,
    });
  });

  it('successful login → useAuth.setSession with full StoredSession + onLoginSuccess fires', async () => {
    const user = userEvent.setup();
    const onLoginSuccess = vi.fn();
    vi.mocked(globalThis.fetch).mockResolvedValue(loginOkResponse());

    render(<LoginScreen onLoginSuccess={onLoginSuccess} />);
    await user.type(screen.getByLabelText(/Email/i), 'owner@yensao.vn');
    await user.type(screen.getByLabelText(/Mật khẩu/i), 'any-password');
    await user.type(screen.getByLabelText(/Workspace/i), 'yensaokhanhhoa');
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    await waitFor(() => expect(onLoginSuccess).toHaveBeenCalled());

    const state = useAuth.getState();
    expect(state.isAuthenticated).toBe(true);
    expect(state.accessToken).toBe(VALID_JWT);
    expect(state.refreshToken).toBe('opaque-refresh-1');
    expect(state.accessTokenExpiresAt).toBe(ACCESS_EXPIRES);
    expect(state.refreshTokenExpiresAt).toBe(REFRESH_EXPIRES);
    expect(state.user?.email).toBe('owner@yensao.vn');
    expect(state.user?.tenantSlug).toBe('yensaokhanhhoa');
    expect(state.user?.role).toBe('Owner');
  });

  // ───────────── Failure paths ─────────────

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
    await user.type(screen.getByLabelText(/Workspace/i), 'yensaokhanhhoa');
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/Invalid credentials/i);
    expect(useAuth.getState().isAuthenticated).toBe(false);
  });

  it('on network failure (fetch rejects), shows generic error', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockRejectedValue(new Error('network kaput'));

    render(<LoginScreen />);
    await user.type(screen.getByLabelText(/Email/i), 'x@y.vn');
    await user.type(screen.getByLabelText(/Mật khẩu/i), 'wrong');
    await user.type(screen.getByLabelText(/Workspace/i), 'yensaokhanhhoa');
    await user.click(screen.getByRole('button', { name: /Đăng nhập$/i }));

    const alert = await screen.findByRole('alert');
    // Generic fallback text — exact phrasing comes from useLocale t()
    // helper; we just assert it surfaces.
    expect(alert).toBeInTheDocument();
    expect(useAuth.getState().isAuthenticated).toBe(false);
  });
});
