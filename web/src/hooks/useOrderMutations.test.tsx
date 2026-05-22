/**
 * useOrderMutations tests — Sprint-7 plan U8 (test-first).
 *
 * Mirrors `useInventoryMutations.test.tsx`'s shape: real fetch is stubbed
 * at the global level so the test exercises the httpClient + idempotency
 * key wiring end-to-end, with TanStack Query invalidation observed via
 * `vi.spyOn(qc, 'invalidateQueries')`.
 *
 * Scenarios covered:
 *   1. Seed POSTs the body to /api/outbound/orders/seed with an
 *      Idempotency-Key header (ULID-shaped).
 *   2. Default body when LineCount is omitted (forwarded as `{}`).
 *   3. Each call generates a different ULID (audit-only dedupe).
 *   4. Success invalidates the broad `['orders']` query key + pushes a
 *      success toast with the new order's ChannelExternalOrderId.
 *   5. 500 error pushes an error toast with idempotencyKey + traceId
 *      (camelCase body.traceId is recognised).
 *   6. 404 with `code: 'environment_not_dev'` surfaces a distinct toast
 *      title — operators in non-Dev environments see a clear message.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import {
  useSeedOrderMutation,
  useConfirmPickMutation,
  useMarkPickFailedMutation,
  useOrderMutations,
} from './useOrderMutations';
import { useToast, __resetToastsForTests } from './useToast';
import { __resetLocaleForTests } from './useLocale';
import { __resetAuthForTests } from './useAuth';

function makeWrapper() {
  const qc = new QueryClient({
    defaultOptions: { mutations: { retry: false }, queries: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
  return { qc, wrapper };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const fetchMock = () => vi.mocked(globalThis.fetch);

const ULID_RE = /^[0-9A-HJKMNP-TV-Z]{26}$/i;

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

// Minimal OrderResponse-shaped fixture for the success path. The
// mutation reads `ChannelExternalOrderId` for the toast body; nothing
// else is inspected. The seed endpoint returns OrderResponse (Sprint-3-
// redux shape) — distinct from OrderDetailDto (Sprint-7 U4).
function fakeSeededOrder(suffix: string) {
  return {
    id: '00000000-0000-0000-0000-00000000aaaa',
    channelExternalOrderId: `SEED_${suffix}`,
    shippingProfile: 'standard',
    status: 'Pending',
    expectedWeightTotal: null,
    actualWeightTotal: null,
    labelUrl: null,
    trackingNumber: null,
    pickWaveId: null,
    lines: [],
  };
}

describe('useSeedOrderMutation — POST contract', () => {
  it('POSTs to /api/outbound/orders/seed with the body and a ULID Idempotency-Key', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('A'), 201));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ lineCount: 3 });
    });

    expect(fetchMock()).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/outbound/orders/seed');
    expect((init as RequestInit).method).toBe('POST');
    const headers = (init as RequestInit).headers as Headers;
    const idemKey = headers.get('Idempotency-Key');
    expect(idemKey).toMatch(ULID_RE);
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      lineCount: 3,
    });
  });

  it('default body when LineCount is omitted (forwarded as {})', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('B'), 201));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync();
    });

    const init = fetchMock().mock.calls[0]![1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({});
    // The header still goes out.
    expect((init.headers as Headers).get('Idempotency-Key')).toMatch(ULID_RE);
  });

  it('each call generates a DIFFERENT idempotency-key (audit-only dedupe)', async () => {
    fetchMock().mockResolvedValue(jsonResponse(fakeSeededOrder('X'), 201));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ lineCount: 1 });
    });
    await act(async () => {
      await result.current.mutateAsync({ lineCount: 1 });
    });

    const k1 = (
      (fetchMock().mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const k2 = (
      (fetchMock().mock.calls[1]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    expect(k1).not.toBeNull();
    expect(k2).not.toBeNull();
    expect(k1).not.toBe(k2);
  });
});

describe('useSeedOrderMutation — onSuccess', () => {
  it('invalidates the broad ["orders"] query key', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('S1'), 201));
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ lineCount: 2 });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders'] });
  });

  it('pushes a success toast carrying the new order ChannelExternalOrderId', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('S2'), 201));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ lineCount: 1 });
    });

    const toasts = useToast.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]!.kind).toBe('success');
    expect(toasts[0]!.body).toBe('SEED_S2');
  });
});

describe('useSeedOrderMutation — onError', () => {
  it('500 → error toast carries the idempotency-key actually sent + traceId (camelCase body.traceId)', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ traceId: 'trace-seed-500', title: 'Boom' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({ lineCount: 3 });
      } catch {
        /* expected */
      }
    });

    const sentKey = (
      (fetchMock().mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const toast = useToast.getState().toasts[0]!;
    expect(toast.kind).toBe('error');
    expect(toast.idempotencyKey).toBe(sentKey);
    expect(toast.traceId).toBe('trace-seed-500');
  });

  it('also reads PascalCase body.TraceId (ASP.NET ProblemDetails)', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ TraceId: 'trace-pascal' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({});
      } catch {
        /* expected */
      }
    });

    expect(useToast.getState().toasts[0]!.traceId).toBe('trace-pascal');
  });

  it('404 with code "environment_not_dev" → distinct toast title (Sprint-7 trade-off carry)', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse(
        {
          code: 'environment_not_dev',
          title: 'Not Found',
          traceId: 'trace-env-404',
        },
        404,
      ),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({ lineCount: 1 });
      } catch {
        /* expected */
      }
    });

    const toast = useToast.getState().toasts[0]!;
    expect(toast.kind).toBe('error');
    // Default locale after __resetLocaleForTests is 'vi' — the VI title
    // for env-not-dev contains "Dev" exactly once; the generic-error
    // title is "Lỗi tạo đơn mẫu" (no "Dev"). So matching "Dev" in the
    // title differentiates the two paths.
    expect(toast.title).toContain('Dev');
    expect(toast.title).not.toContain('Lỗi tạo đơn mẫu');
    expect(toast.traceId).toBe('trace-env-404');
  });

  it('mutation reports failure (Promise rejected) on non-2xx', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse({}, 500));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSeedOrderMutation(), { wrapper });

    let caught: unknown = null;
    await act(async () => {
      try {
        await result.current.mutateAsync({});
      } catch (e) {
        caught = e;
      }
    });

    expect(caught).not.toBeNull();
  });
});

// ── Sprint-11 U2 — confirmPick + markPickFailed ──────────────────────────

describe('useConfirmPickMutation (Sprint-11 U2)', () => {
  const ORDER_ID = '01HABC1234567890ABCDEFGHIJ';

  it('POSTs to /api/outbound/orders/{id}/confirm-pick with a ULID Idempotency-Key', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('CP'), 200));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useConfirmPickMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ orderId: ORDER_ID });
    });

    expect(fetchMock()).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain(
      `/api/outbound/orders/${encodeURIComponent(ORDER_ID)}/confirm-pick`,
    );
    expect((init as RequestInit).method).toBe('POST');
    expect(
      ((init as RequestInit).headers as Headers).get('Idempotency-Key'),
    ).toMatch(ULID_RE);
  });

  it('accepts a bare string variable as a convenience (route-id shorthand)', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('CP2'), 200));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useConfirmPickMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync(ORDER_ID);
    });

    const [url] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain(`/${encodeURIComponent(ORDER_ID)}/confirm-pick`);
  });

  it('each call generates a different ULID idempotency-key', async () => {
    // Fresh Response per call — Body can only be read once; reusing one
    // Response across two calls would trip "Body is unusable" (same root
    // cause as the Sprint-7 baseline test above this section).
    fetchMock().mockImplementation(() =>
      Promise.resolve(jsonResponse(fakeSeededOrder('CP3'), 200)),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useConfirmPickMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ orderId: ORDER_ID });
    });
    await act(async () => {
      await result.current.mutateAsync({ orderId: ORDER_ID });
    });

    const k1 = (
      (fetchMock().mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const k2 = (
      (fetchMock().mock.calls[1]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    expect(k1).not.toBe(k2);
  });

  it('on success: invalidates orders + order-detail + order-transitions + pushes success toast', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('CP4'), 200));
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useConfirmPickMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ orderId: ORDER_ID });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['order-detail'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['order-transitions'] });

    const toast = useToast.getState().toasts[0]!;
    expect(toast.kind).toBe('success');
    expect(toast.title).toContain('Xác nhận lấy hàng');
  });

  it('on 403: pushes an error toast with idempotency-key + traceId', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ traceId: 'trace-pick-403', title: 'Forbidden' }, 403),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useConfirmPickMutation(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({ orderId: ORDER_ID });
      } catch {
        /* expected */
      }
    });

    const sentKey = (
      (fetchMock().mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const toast = useToast.getState().toasts[0]!;
    expect(toast.kind).toBe('error');
    expect(toast.idempotencyKey).toBe(sentKey);
    expect(toast.traceId).toBe('trace-pick-403');
  });

  it('on 500: pushes an error toast with traceId', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ TraceId: 'trace-pick-500' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useConfirmPickMutation(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({ orderId: ORDER_ID });
      } catch {
        /* expected */
      }
    });

    expect(useToast.getState().toasts[0]!.traceId).toBe('trace-pick-500');
  });
});

describe('useMarkPickFailedMutation (Sprint-11 U2)', () => {
  const ORDER_ID = '01HABC9876543210ZYXWVUTSRQ';

  it('POSTs to /mark-pick-failed with { reason } body + ULID Idempotency-Key', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('MF'), 200));
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useMarkPickFailedMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        orderId: ORDER_ID,
        reason: 'Out of stock on shelf',
      });
    });

    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain(
      `/api/outbound/orders/${encodeURIComponent(ORDER_ID)}/mark-pick-failed`,
    );
    expect((init as RequestInit).method).toBe('POST');
    expect(
      ((init as RequestInit).headers as Headers).get('Idempotency-Key'),
    ).toMatch(ULID_RE);
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      reason: 'Out of stock on shelf',
    });
  });

  it('on success: invalidates orders + order-detail + order-transitions + pushes success toast', async () => {
    fetchMock().mockResolvedValueOnce(jsonResponse(fakeSeededOrder('MF2'), 200));
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useMarkPickFailedMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        orderId: ORDER_ID,
        reason: 'Damaged box',
      });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['order-detail'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['order-transitions'] });

    const toast = useToast.getState().toasts[0]!;
    expect(toast.kind).toBe('success');
    expect(toast.title).toContain('báo lỗi lấy hàng');
  });

  it('on 500: error toast carries idempotency-key + traceId', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ traceId: 'trace-mf-500' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useMarkPickFailedMutation(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({ orderId: ORDER_ID, reason: 'x' });
      } catch {
        /* expected */
      }
    });

    const sentKey = (
      (fetchMock().mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const toast = useToast.getState().toasts[0]!;
    expect(toast.kind).toBe('error');
    expect(toast.idempotencyKey).toBe(sentKey);
    expect(toast.traceId).toBe('trace-mf-500');
  });
});

describe('useOrderMutations aggregator (Sprint-11 U2)', () => {
  it('exposes seedOrder + confirmPick + markPickFailed handles', () => {
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useOrderMutations(), { wrapper });

    expect(result.current.seedOrder).toBeDefined();
    expect(result.current.confirmPick).toBeDefined();
    expect(result.current.markPickFailed).toBeDefined();
    expect(typeof result.current.confirmPick.mutate).toBe('function');
    expect(typeof result.current.markPickFailed.mutate).toBe('function');
  });
});
