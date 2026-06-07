import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useInventoryMutations } from './useInventoryMutations';
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

function noBodyResponse(status = 204): Response {
  return new Response(null, { status });
}

const fetchMock = () => vi.mocked(globalThis.fetch);

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

describe('useInventoryMutations.adjust', () => {
  it('POSTs to /api/v1/inventory/adjustments with the body and an Idempotency-Key header', async () => {
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.adjust.mutateAsync({
        sku: 'YN-001',
        delta: 10,
        reason: 'recount',
      });
    });

    expect(fetchMock()).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/adjustments');
    expect((init as RequestInit).method).toBe('POST');
    const headers = (init as RequestInit).headers as Headers;
    const idemKey = headers.get('Idempotency-Key');
    expect(idemKey).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/i);
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      sku: 'YN-001',
      delta: 10,
      reason: 'recount',
    });
  });

  it('on success, invalidates inventory skus + summary + ledger queries', async () => {
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.adjust.mutateAsync({
        sku: 'YN-001',
        delta: 5,
        reason: 'recount',
      });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory', 'skus'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory', 'summary'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory', 'ledger'] });
  });

  it('on success, pushes a success toast that references the SKU + signed delta', async () => {
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.adjust.mutateAsync({
        sku: 'YN-001',
        delta: 5,
        reason: 'recount',
      });
    });

    const toasts = useToast.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]!.kind).toBe('success');
    expect(toasts[0]!.body).toContain('YN-001');
    expect(toasts[0]!.body).toContain('+5');
  });

  it('a negative delta renders as -N in the success toast body', async () => {
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.adjust.mutateAsync({
        sku: 'YN-001',
        delta: -3,
        reason: 'damage',
      });
    });

    expect(useToast.getState().toasts[0]!.body).toContain('-3');
  });

  it('on error, pushes an error toast with idempotency-key + trace-id (camelCase body.traceId)', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ traceId: 'trace-xyz', title: 'Boom' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      try {
        await result.current.adjust.mutateAsync({
          sku: 'YN-001',
          delta: 5,
          reason: 'recount',
        });
      } catch {
        /* expected */
      }
    });

    const toasts = useToast.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]!.kind).toBe('error');
    expect(toasts[0]!.idempotencyKey).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/i);
    expect(toasts[0]!.traceId).toBe('trace-xyz');
  });

  it('also reads PascalCase body.TraceId (ASP.NET ProblemDetails)', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ TraceId: 'trace-pascal' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      try {
        await result.current.adjust.mutateAsync({
          sku: 'YN-001',
          delta: 5,
          reason: 'recount',
        });
      } catch {
        /* expected */
      }
    });

    expect(useToast.getState().toasts[0]!.traceId).toBe('trace-pascal');
  });

  it('each mutate call generates a DIFFERENT idempotency-key (per Sprint-6 trade-off #2 — audit-only dedup)', async () => {
    fetchMock().mockResolvedValue(noBodyResponse());
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.adjust.mutateAsync({ sku: 'A', delta: 5, reason: 'recount' });
    });
    await act(async () => {
      await result.current.adjust.mutateAsync({ sku: 'A', delta: 5, reason: 'recount' });
    });

    const init1 = fetchMock().mock.calls[0]![1] as RequestInit;
    const init2 = fetchMock().mock.calls[1]![1] as RequestInit;
    const key1 = (init1.headers as Headers).get('Idempotency-Key');
    const key2 = (init2.headers as Headers).get('Idempotency-Key');
    expect(key1).not.toBeNull();
    expect(key2).not.toBeNull();
    expect(key1).not.toBe(key2);
  });
});

describe('useInventoryMutations.setThreshold', () => {
  it('PUTs to /api/v1/inventory/skus/:sku/threshold with body { threshold }', async () => {
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.setThreshold.mutateAsync({ sku: 'YN-001', threshold: 50 });
    });

    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/skus/YN-001/threshold');
    expect((init as RequestInit).method).toBe('PUT');
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({ threshold: 50 });
    expect(((init as RequestInit).headers as Headers).get('Idempotency-Key')).toMatch(
      /^[0-9A-HJKMNP-TV-Z]{26}$/i,
    );
  });

  it('on success, invalidates inventory queries + pushes a success toast', async () => {
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      await result.current.setThreshold.mutateAsync({ sku: 'YN-001', threshold: 50 });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory', 'skus'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory', 'summary'] });
    expect(useToast.getState().toasts[0]!.kind).toBe('success');
  });

  it('on error, shows error toast carrying the idempotency-key the request actually sent', async () => {
    fetchMock().mockResolvedValueOnce(
      jsonResponse({ traceId: 'trace-th' }, 500),
    );
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventoryMutations(), { wrapper });

    await act(async () => {
      try {
        await result.current.setThreshold.mutateAsync({ sku: 'YN-001', threshold: 50 });
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
    expect(toast.traceId).toBe('trace-th');
  });
});
