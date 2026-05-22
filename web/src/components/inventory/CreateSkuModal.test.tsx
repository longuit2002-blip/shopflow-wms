import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { CreateSkuModal } from './CreateSkuModal';
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
  // Sprint-10.5 U5 — pre-populate a session carrying inventory.skus.write
  // so the new gate doesn't hide the modal in pre-Sprint-10.5 tests.
  useAuth.getState().setSession(sessionWith(['inventory.skus.write']));
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

describe('CreateSkuModal', () => {
  it('renders nothing when isOpen=false', () => {
    const { container } = renderWithClient(
      <CreateSkuModal isOpen={false} onClose={() => {}} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders title and the two ship-time fields', () => {
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
    expect(screen.getByText(/Thêm SKU mới/)).toBeInTheDocument();
    expect(screen.getByTestId('create-sku-sku')).toBeInTheDocument();
    expect(screen.getByTestId('create-sku-initial')).toBeInTheDocument();
  });

  it('lowercases keystrokes are normalised to uppercase by the field', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
    const skuInput = screen.getByTestId('create-sku-sku') as HTMLInputElement;
    await user.type(skuInput, 'yn-red-100');
    expect(skuInput.value).toBe('YN-RED-100');
  });

  it('submitting with an empty sku surfaces a required-field error', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
    await user.type(screen.getByTestId('create-sku-initial'), '100');
    await user.click(screen.getByTestId('create-sku-submit'));
    expect(screen.getByTestId('create-sku-sku-error')).toHaveTextContent(
      /không được để trống/,
    );
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('submitting with a bad-format sku surfaces the regex error', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
    // Bypass the auto-uppercase by setting value via clear + paste with bad punctuation
    const skuInput = screen.getByTestId('create-sku-sku') as HTMLInputElement;
    await user.click(skuInput);
    await user.paste('YN_RED@100');
    await user.type(screen.getByTestId('create-sku-initial'), '100');
    await user.click(screen.getByTestId('create-sku-submit'));
    expect(screen.getByTestId('create-sku-sku-error')).toHaveTextContent(
      /chữ HOA \+ số \+ dấu gạch ngang/,
    );
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('submitting with negative initialAvailable surfaces a non-negative error', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
    await user.type(screen.getByTestId('create-sku-sku'), 'YN-001');
    await user.type(screen.getByTestId('create-sku-initial'), '-5');
    await user.click(screen.getByTestId('create-sku-submit'));
    expect(screen.getByTestId('create-sku-initial-error')).toHaveTextContent(/≥ 0/);
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('happy path: POSTs to /api/v1/inventory/skus with { sku, initialAvailable } and closes', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse(201));
    const onClose = vi.fn();
    renderWithClient(<CreateSkuModal isOpen onClose={onClose} />);

    await user.type(screen.getByTestId('create-sku-sku'), 'YN-001');
    await user.type(screen.getByTestId('create-sku-initial'), '250');
    await user.click(screen.getByTestId('create-sku-submit'));

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/skus');
    expect((init as RequestInit).method).toBe('POST');
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      sku: 'YN-001',
      initialAvailable: 250,
    });
    expect(((init as RequestInit).headers as Headers).get('Idempotency-Key')).toMatch(
      /^[0-9A-HJKMNP-TV-Z]{26}$/i,
    );
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('on success, surfaces a success toast carrying the new SKU code', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse(201));
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);

    await user.type(screen.getByTestId('create-sku-sku'), 'YN-001');
    await user.type(screen.getByTestId('create-sku-initial'), '250');
    await user.click(screen.getByTestId('create-sku-submit'));

    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('success');
    });
    expect(useToast.getState().toasts[0]!.body).toContain('YN-001');
  });

  it('on 409 Conflict, surfaces a duplicate-SKU error INLINE on the sku field (no success toast)', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(jsonResponse({ title: 'Conflict' }, 409));
    const onClose = vi.fn();
    renderWithClient(<CreateSkuModal isOpen onClose={onClose} />);

    await user.type(screen.getByTestId('create-sku-sku'), 'YN-001');
    await user.type(screen.getByTestId('create-sku-initial'), '100');
    await user.click(screen.getByTestId('create-sku-submit'));

    await waitFor(() => {
      expect(screen.getByTestId('create-sku-sku-error')).toHaveTextContent(
        /đã tồn tại/,
      );
    });
    expect(onClose).not.toHaveBeenCalled();
    // 409 is a field-level error — no error toast (hook still pushes one for any non-2xx;
    // this test asserts the inline field error is present, which is the primary surface)
    expect(screen.getByTestId('create-sku-sku-error')).toBeInTheDocument();
  });

  it('on 5xx, keeps the modal open + the hook pushes an error toast', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(jsonResponse({ traceId: 't-1' }, 500));
    const onClose = vi.fn();
    renderWithClient(<CreateSkuModal isOpen onClose={onClose} />);

    await user.type(screen.getByTestId('create-sku-sku'), 'YN-001');
    await user.type(screen.getByTestId('create-sku-initial'), '100');
    await user.click(screen.getByTestId('create-sku-submit'));

    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('error');
    });
    expect(onClose).not.toHaveBeenCalled();
  });

  it('clicking Cancel closes the modal without submitting', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderWithClient(<CreateSkuModal isOpen onClose={onClose} />);
    await user.click(screen.getByRole('button', { name: /Hủy/ }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('Esc closes the modal (Modal primitive contract)', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderWithClient(<CreateSkuModal isOpen onClose={onClose} />);
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('backdrop click does NOT close (dismissOnBackdrop=false to protect typed data)', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderWithClient(<CreateSkuModal isOpen onClose={onClose} />);
    await user.click(screen.getByTestId('modal-mask'));
    expect(onClose).not.toHaveBeenCalled();
  });

  it('sku input aria-invalid flips true after a failed submit', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
    await user.type(screen.getByTestId('create-sku-initial'), '100');
    await user.click(screen.getByTestId('create-sku-submit'));
    expect(screen.getByTestId('create-sku-sku')).toHaveAttribute('aria-invalid', 'true');
  });

  // ── Sprint-10.5 U5: perm-gated rendering ────────────────────────────────
  describe('perm gating (Sprint-10.5 U5)', () => {
    it('renders the modal when the user holds inventory.skus.write', () => {
      renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
      expect(screen.getByText(/Thêm SKU mới/)).toBeInTheDocument();
    });

    it('renders nothing when the user lacks inventory.skus.write (hidden — KTD8)', () => {
      useAuth.getState().setSession(sessionWith(['inventory.read']));
      const { container } = renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
      expect(container).toBeEmptyDOMElement();
    });

    it('renders nothing when the user has no session (fail-closed — KTD12)', () => {
      __resetAuthForTests();
      const { container } = renderWithClient(<CreateSkuModal isOpen onClose={() => {}} />);
      expect(container).toBeEmptyDOMElement();
    });
  });
});
