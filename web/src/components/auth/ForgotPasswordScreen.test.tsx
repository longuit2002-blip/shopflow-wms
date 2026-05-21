import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { ForgotPasswordScreen } from './ForgotPasswordScreen';
import { __resetLocaleForTests } from '../../hooks/useLocale';

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

describe('ForgotPasswordScreen (Sprint-9.5 U6)', () => {
  beforeEach(() => {
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
    stubHostname('localhost');
  });
  afterEach(() => {
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  it('renders email + workspace fields + submit', () => {
    render(<ForgotPasswordScreen />);
    expect(screen.getByLabelText(/Email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Workspace/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Send reset link|Gửi liên kết/i }),
    ).toBeInTheDocument();
  });

  it('submit POSTs /api/auth/forgot-password with email + workspace', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(new Response('{}', { status: 200 }));

    render(<ForgotPasswordScreen />);
    await user.type(screen.getByLabelText(/Email/i), 'alice@example.com');
    await user.type(screen.getByLabelText(/Workspace/i), 'tenant-a');
    await user.click(
      screen.getByRole('button', { name: /Send reset link|Gửi liên kết/i }),
    );

    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalledTimes(1));
    const fetchMock = vi.mocked(globalThis.fetch);
    const call = fetchMock.mock.calls[0];
    expect(call[0]).toBe('/api/auth/forgot-password');
    const body = JSON.parse((call[1] as RequestInit).body as string);
    expect(body).toEqual({ email: 'alice@example.com', tenantSlug: 'tenant-a' });
  });

  it('always shows success state regardless of API response (R6 enumeration discipline)', async () => {
    const user = userEvent.setup();
    // Backend returns a 422 — UI must still display success.
    vi.mocked(globalThis.fetch).mockResolvedValue(new Response('{}', { status: 422 }));

    render(<ForgotPasswordScreen />);
    await user.type(screen.getByLabelText(/Email/i), 'alice@example.com');
    await user.type(screen.getByLabelText(/Workspace/i), 'tenant-a');
    await user.click(
      screen.getByRole('button', { name: /Send reset link|Gửi liên kết/i }),
    );

    const status = await screen.findByRole('status');
    expect(status.textContent).toMatch(/reset link within 5 minutes|liên kết đặt lại/i);
  });

  it('workspace auto-detects from non-reserved subdomain', () => {
    stubHostname('tenant-a.shopflow.local');
    render(<ForgotPasswordScreen />);
    const workspaceField = screen.getByLabelText(/Workspace/i) as HTMLInputElement;
    expect(workspaceField.value).toBe('tenant-a');
    expect(workspaceField.readOnly).toBe(true);
  });
});
