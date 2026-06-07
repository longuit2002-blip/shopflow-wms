import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { FlashSaleToggle } from './FlashSaleToggle';
import { useToast, __resetToastsForTests } from '../../hooks/useToast';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';

// Sprint-10.5 U5 — JWT-with-perm helper.
function jwtWithPerm(perm: string[]): string {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const payload = btoa(
    JSON.stringify({
      sub: '8f72f516-cc02-4f54-9cc8-be0f3b96c4f3',
      email: 'owner@yensao.vn',
      role: 'Owner',
      tenant_slug: 'yensaokhanhhoa',
      perm,
      exp: 9999999999,
    }),
  );
  return `${header}.${payload}.signature`;
}

function sessionWith(perm: string[]) {
  return {
    accessToken: jwtWithPerm(perm),
    refreshToken: 'opaque',
    accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({
    defaultOptions: { mutations: { retry: false }, queries: { retry: false } },
  });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

function noBodyResponse(status = 204): Response {
  return new Response(null, { status });
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const fetchMock = () => vi.mocked(globalThis.fetch);

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  // Sprint-10.5 U5 — without a perm-carrying session the new gate falls
  // back to the static <Pill> (DL-002), breaking existing toggle tests.
  useAuth.getState().setSession(sessionWith(['inventory.skus.flash-sale.write']));
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

describe('FlashSaleToggle', () => {
  it('renders the Toggle reflecting the prop value (off)', () => {
    renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);
    expect(screen.getByTestId('flash-toggle-YN-001')).toHaveAttribute(
      'aria-checked',
      'false',
    );
  });

  it('renders the Toggle reflecting the prop value (on)', () => {
    renderWithClient(<FlashSaleToggle sku="YN-001" value={true} />);
    expect(screen.getByTestId('flash-toggle-YN-001')).toHaveAttribute(
      'aria-checked',
      'true',
    );
  });

  it('clicking the toggle PUTs /api/v1/inventory/skus/{sku}/flash-sale with body.active', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);

    await user.click(screen.getByTestId('flash-toggle-YN-001'));

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/skus/YN-001/flash-sale');
    expect((init as RequestInit).method).toBe('PUT');
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({ active: true });
  });

  it('optimistic UI: the toggle visual flips immediately, BEFORE the network response', async () => {
    const user = userEvent.setup();
    let resolveFetch!: (r: Response) => void;
    fetchMock().mockReturnValueOnce(
      new Promise<Response>((res) => {
        resolveFetch = res;
      }),
    );
    renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);
    await user.click(screen.getByTestId('flash-toggle-YN-001'));

    expect(screen.getByTestId('flash-toggle-YN-001')).toHaveAttribute(
      'aria-checked',
      'true',
    );
    act(() => {
      resolveFetch(noBodyResponse());
    });
  });

  it('on failure, reverts the optimistic value back to the server value + pushes an error toast', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(jsonResponse({ traceId: 't-flash' }, 500));
    renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);
    await user.click(screen.getByTestId('flash-toggle-YN-001'));

    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('error');
    });
    expect(screen.getByTestId('flash-toggle-YN-001')).toHaveAttribute(
      'aria-checked',
      'false',
    );
  });

  it('toggle is disabled while the mutation is pending (anti-double-click)', async () => {
    const user = userEvent.setup();
    let resolveFetch!: (r: Response) => void;
    fetchMock().mockReturnValueOnce(
      new Promise<Response>((res) => {
        resolveFetch = res;
      }),
    );
    renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);
    await user.click(screen.getByTestId('flash-toggle-YN-001'));

    expect(screen.getByTestId('flash-toggle-YN-001')).toBeDisabled();
    act(() => {
      resolveFetch(noBodyResponse());
    });
  });

  it('a parent re-render with a changed server value resyncs the optimistic state', async () => {
    const { rerender } = renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);
    expect(screen.getByTestId('flash-toggle-YN-001')).toHaveAttribute(
      'aria-checked',
      'false',
    );
    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <FlashSaleToggle sku="YN-001" value={true} />
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('flash-toggle-YN-001')).toHaveAttribute(
      'aria-checked',
      'true',
    );
  });

  // ── Sprint-10.5 U5: perm-gated toggle with DL-002 Pill fallback ──────────
  describe('perm gating (Sprint-10.5 U5)', () => {
    it('renders the toggle when the user holds inventory.skus.flash-sale.write', () => {
      // beforeEach already sets the perm.
      renderWithClient(<FlashSaleToggle sku="YN-001" value={true} />);
      expect(screen.getByTestId('flash-toggle-YN-001')).toBeInTheDocument();
      expect(screen.queryByTestId('flash-status-YN-001')).not.toBeInTheDocument();
    });

    it('renders the static <Pill> fallback (DL-002) when the user lacks the perm — On state', () => {
      useAuth.getState().setSession(sessionWith(['inventory.read']));
      renderWithClient(<FlashSaleToggle sku="YN-001" value={true} />);
      const pill = screen.getByTestId('flash-status-YN-001');
      expect(pill).toBeInTheDocument();
      expect(pill).toHaveTextContent(/Bật|On/);
      // Interactive toggle is hidden.
      expect(screen.queryByTestId('flash-toggle-YN-001')).not.toBeInTheDocument();
    });

    it('renders the static <Pill> showing Off when the user lacks the perm + value=false', () => {
      useAuth.getState().setSession(sessionWith(['inventory.read']));
      renderWithClient(<FlashSaleToggle sku="YN-001" value={false} />);
      expect(screen.getByTestId('flash-status-YN-001')).toHaveTextContent(/Tắt|Off/);
    });

    it('no session → DL-002 Pill fallback (fail-closed — KTD12)', () => {
      __resetAuthForTests();
      renderWithClient(<FlashSaleToggle sku="YN-001" value={true} />);
      expect(screen.getByTestId('flash-status-YN-001')).toBeInTheDocument();
    });
  });
});
