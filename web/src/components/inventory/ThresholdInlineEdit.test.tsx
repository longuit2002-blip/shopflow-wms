import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { ThresholdInlineEdit } from './ThresholdInlineEdit';
import { useToast, __resetToastsForTests } from '../../hooks/useToast';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';

// Sprint-10.5 U5 — JWT-with-perm helper so the component's
// usePerm('inventory.skus.threshold.write') call sees the key.
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
  // Sprint-10.5 U5 — existing tests assume the editable button is the
  // primary rendered surface. Without a perm-holding session the new
  // gate falls back to the static <span> DL-001 path.
  useAuth.getState().setSession(sessionWith(['inventory.skus.threshold.write']));
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

describe('ThresholdInlineEdit', () => {
  it('renders the current value as a clickable cell', () => {
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('renders an em-dash when value is null', () => {
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={null} />);
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('—');
  });

  it('clicking the cell enters edit mode and focuses the input', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001');
    expect(input).toBeInTheDocument();
    expect(document.activeElement).toBe(input);
  });

  it('Enter commits the new value via PUT', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);

    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '75');
    await user.keyboard('{Enter}');

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/skus/YN-001/threshold');
    expect((init as RequestInit).method).toBe('PUT');
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({ threshold: 75 });
  });

  it('blur commits the new value via PUT', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    renderWithClient(
      <>
        <ThresholdInlineEdit sku="YN-001" value={50} />
        <input data-testid="next-input" />
      </>,
    );
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '75');
    await user.click(screen.getByTestId('next-input'));

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
  });

  it('Esc reverts WITHOUT calling the API', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '999');
    await user.keyboard('{Escape}');

    expect(fetchMock()).not.toHaveBeenCalled();
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('committing the same value does NOT call the API (no-op short-circuit)', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    await user.keyboard('{Enter}');
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('shows the optimistic value immediately on commit (before the network roundtrips)', async () => {
    const user = userEvent.setup();
    let resolveFetch!: (r: Response) => void;
    fetchMock().mockReturnValueOnce(
      new Promise<Response>((res) => {
        resolveFetch = res;
      }),
    );
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '88');
    await user.keyboard('{Enter}');

    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('88');
    // Now resolve the request — no errors should appear.
    act(() => {
      resolveFetch(noBodyResponse());
    });
  });

  it('on a 5xx failure, reverts the optimistic value back to the server value and surfaces an error toast', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(jsonResponse({ traceId: 't-1' }, 500));
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);

    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '88');
    await user.keyboard('{Enter}');

    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('error');
    });
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('rejects negative input by reverting on commit', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '-5');
    await user.keyboard('{Enter}');
    expect(fetchMock()).not.toHaveBeenCalled();
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('the cell button has an aria-label including the SKU (keyboard reachable)', () => {
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveAttribute(
      'aria-label',
      expect.stringContaining('YN-001'),
    );
  });

  it('the input has an aria-label including the SKU', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    expect(screen.getByTestId('threshold-input-YN-001')).toHaveAttribute(
      'aria-label',
      expect.stringContaining('YN-001'),
    );
  });

  // ── Sprint-10.5 U5: perm-gated editing with DL-001 static fallback ───────
  describe('perm gating (Sprint-10.5 U5)', () => {
    it('renders the editable button when the user holds inventory.skus.threshold.write', () => {
      // beforeEach already sets the perm.
      renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
      expect(screen.getByTestId('threshold-cell-YN-001')).toBeInTheDocument();
      expect(screen.queryByTestId('threshold-static-YN-001')).not.toBeInTheDocument();
    });

    it('renders the static <span> fallback (DL-001) when the user lacks the perm', () => {
      useAuth.getState().setSession(sessionWith(['inventory.read']));
      renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
      const span = screen.getByTestId('threshold-static-YN-001');
      expect(span).toBeInTheDocument();
      expect(span.tagName.toLowerCase()).toBe('span');
      expect(span).toHaveTextContent('50');
      // The interactive button is hidden.
      expect(screen.queryByTestId('threshold-cell-YN-001')).not.toBeInTheDocument();
    });

    it('static fallback renders em-dash when value is null', () => {
      useAuth.getState().setSession(sessionWith(['inventory.read']));
      renderWithClient(<ThresholdInlineEdit sku="YN-001" value={null} />);
      expect(screen.getByTestId('threshold-static-YN-001')).toHaveTextContent('—');
    });

    it('no session → static <span> fallback (fail-closed — KTD12)', () => {
      __resetAuthForTests();
      renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
      expect(screen.getByTestId('threshold-static-YN-001')).toBeInTheDocument();
    });
  });
});
