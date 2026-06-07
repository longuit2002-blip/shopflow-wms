import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { AdjustStockModal } from './AdjustStockModal';
import { useToast, __resetToastsForTests } from '../../hooks/useToast';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';

// Sprint-10.5 U5 — encode a JWT with the given perm[] keys so the
// component's usePerm('inventory.adjust') call sees them. `setSession`
// then puts the store in the right shape. Signature unverified — the
// client-side decoder only reads payload claims.
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
  // Sprint-10.5 U5 — pre-Sprint-10.5 tests assumed the modal always
  // renders. The new usePerm('inventory.adjust') gate would hide the
  // modal under __resetAuthForTests's null user, breaking every test.
  // Pre-populate a session that carries the required perm.
  useAuth.getState().setSession(sessionWith(['inventory.adjust']));
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

describe('AdjustStockModal', () => {
  it('renders nothing when isOpen=false', () => {
    const { container } = renderWithClient(
      <AdjustStockModal isOpen={false} onClose={() => {}} sku="YN-001" />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders title with the SKU when open', () => {
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);
    expect(screen.getByText(/Điều chỉnh tồn · YN-001/)).toBeInTheDocument();
  });

  it('submit is disabled when delta is empty or reason is unselected', () => {
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);
    expect(screen.getByTestId('adjust-submit')).toBeDisabled();
  });

  it('submit stays disabled with delta=0', async () => {
    const user = userEvent.setup();
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);
    await user.type(screen.getByTestId('adjust-delta'), '0');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'recount');
    expect(screen.getByTestId('adjust-submit')).toBeDisabled();
  });

  it('submit enables with non-zero delta + reason selected', async () => {
    const user = userEvent.setup();
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);
    await user.type(screen.getByTestId('adjust-delta'), '10');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'recount');
    expect(screen.getByTestId('adjust-submit')).toBeEnabled();
  });

  it('submitting the form POSTs to /adjustments with delta + reason + (optional) note', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const onClose = vi.fn();
    renderWithClient(<AdjustStockModal isOpen onClose={onClose} sku="YN-001" />);

    await user.type(screen.getByTestId('adjust-delta'), '10');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'recount');
    await user.type(screen.getByTestId('adjust-note'), 'kiểm kho cuối tuần');
    await user.click(screen.getByTestId('adjust-submit'));

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
    const [, init] = fetchMock().mock.calls[0]!;
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      sku: 'YN-001',
      delta: 10,
      reason: 'recount',
      note: 'kiểm kho cuối tuần',
    });
  });

  it('omits the note field when the user leaves it blank', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);

    await user.type(screen.getByTestId('adjust-delta'), '-3');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'damage');
    await user.click(screen.getByTestId('adjust-submit'));

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
    const body = JSON.parse(
      (fetchMock().mock.calls[0]![1] as RequestInit).body as string,
    );
    expect(body).toEqual({ sku: 'YN-001', delta: -3, reason: 'damage' });
    expect(body).not.toHaveProperty('note');
  });

  it('closes the modal on success', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const onClose = vi.fn();
    renderWithClient(<AdjustStockModal isOpen onClose={onClose} sku="YN-001" />);

    await user.type(screen.getByTestId('adjust-delta'), '5');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'found');
    await user.click(screen.getByTestId('adjust-submit'));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('keeps the modal open on a 500 failure so the user can retry', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(jsonResponse({ traceId: 't-1' }, 500));
    const onClose = vi.fn();
    renderWithClient(<AdjustStockModal isOpen onClose={onClose} sku="YN-001" />);

    await user.type(screen.getByTestId('adjust-delta'), '5');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'recount');
    await user.click(screen.getByTestId('adjust-submit'));

    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('error');
    });
    expect(onClose).not.toHaveBeenCalled();
    // Form state survives — delta + reason are still populated.
    expect(screen.getByTestId('adjust-delta')).toHaveValue(5);
    expect(screen.getByTestId('adjust-reason')).toHaveValue('recount');
  });

  it('retrying after failure sends a NEW Idempotency-Key (audit-only dedup, Sprint-6 trade-off #2)', async () => {
    const user = userEvent.setup();
    fetchMock()
      .mockResolvedValueOnce(jsonResponse({ traceId: 't-1' }, 500))
      .mockResolvedValueOnce(noBodyResponse());
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);

    await user.type(screen.getByTestId('adjust-delta'), '5');
    await user.selectOptions(screen.getByTestId('adjust-reason'), 'recount');
    await user.click(screen.getByTestId('adjust-submit'));
    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('error');
    });
    act(() => {
      __resetToastsForTests();
    });
    await user.click(screen.getByTestId('adjust-submit'));
    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(2);
    });

    const headers1 = (fetchMock().mock.calls[0]![1] as RequestInit).headers as Headers;
    const headers2 = (fetchMock().mock.calls[1]![1] as RequestInit).headers as Headers;
    expect(headers1.get('Idempotency-Key')).not.toBe(headers2.get('Idempotency-Key'));
  });

  it('clicking Cancel closes the modal without submitting', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderWithClient(<AdjustStockModal isOpen onClose={onClose} sku="YN-001" />);
    await user.click(screen.getByRole('button', { name: /Hủy/ }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('Esc closes the modal (Modal primitive contract)', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderWithClient(<AdjustStockModal isOpen onClose={onClose} sku="YN-001" />);
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('backdrop click does NOT close (dismissOnBackdrop=false to protect typed data)', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderWithClient(<AdjustStockModal isOpen onClose={onClose} sku="YN-001" />);
    await user.click(screen.getByTestId('modal-mask'));
    expect(onClose).not.toHaveBeenCalled();
  });

  it('note input enforces the 240-char max length', async () => {
    const user = userEvent.setup();
    renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);
    const longText = 'x'.repeat(300);
    await user.type(screen.getByTestId('adjust-note'), longText);
    expect((screen.getByTestId('adjust-note') as HTMLTextAreaElement).value).toHaveLength(
      240,
    );
  });

  // ── Sprint-10.5 U5: perm-gated rendering ────────────────────────────────
  describe('perm gating (Sprint-10.5 U5)', () => {
    it('renders the modal when the user holds inventory.adjust', () => {
      // beforeEach already sets the perm; assert the title surface renders.
      renderWithClient(<AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />);
      expect(screen.getByText(/Điều chỉnh tồn · YN-001/)).toBeInTheDocument();
    });

    it('renders nothing when the user lacks inventory.adjust (hidden — KTD8)', () => {
      // Narrow the session to a perm[] without the key.
      useAuth.getState().setSession(sessionWith(['inventory.read']));
      const { container } = renderWithClient(
        <AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />,
      );
      expect(container).toBeEmptyDOMElement();
    });

    it('renders nothing when the user has no session (fail-closed — KTD12)', () => {
      __resetAuthForTests();
      const { container } = renderWithClient(
        <AdjustStockModal isOpen onClose={() => {}} sku="YN-001" />,
      );
      expect(container).toBeEmptyDOMElement();
    });
  });
});
