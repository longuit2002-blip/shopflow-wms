/**
 * useInventoryQuery tests — Sprint-7 plan U9.
 *
 * Verifies the SignalR wire-up layered onto Sprint-6's polling base:
 *
 *   - `stock_changed` hub event → broad `['inventory']` invalidation
 *   - hub state `'connected'` → `refetchInterval === false` (polling off)
 *   - hub state `'disconnected'` (and every non-connected state) →
 *     `refetchInterval === POLL_MS` (polling fallback per R13)
 *   - mount with JWT → `connect()` triggered exactly once
 *   - unmount → subscription cleanup runs (unsubscribe fn returned by
 *     `useSignalR.subscribe` is invoked)
 *
 * The `@/hooks/useSignalR` module is mocked at the boundary — we don't
 * exercise the underlying `@microsoft/signalr` SDK here (that's covered
 * by `useSignalR.test.tsx`). This keeps the U9 tests focused on the
 * inventory-side contract.
 *
 * Pattern echo: U7's `vi.hoisted` ref pattern is reused so the test
 * file can both drive the mock from inside `vi.mock` AND read the same
 * state from assertions after-the-fact.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

// ---------------------------------------------------------------------------
// useSignalR mock
// ---------------------------------------------------------------------------

type Handler = (payload: unknown) => void;

interface SignalRMock {
  state: 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';
  connectCalls: number;
  subscribeCalls: { eventName: string; handler: Handler }[];
  handlers: Map<string, Set<Handler>>;
  unsubscribeFns: ReturnType<typeof vi.fn>[];
}

const signalrRef = vi.hoisted(() => {
  return { current: null as unknown as SignalRMock };
});

function makeSignalRMock(): SignalRMock {
  const handlers = new Map<string, Set<Handler>>();
  return {
    state: 'idle',
    connectCalls: 0,
    subscribeCalls: [],
    handlers,
    unsubscribeFns: [],
  };
}

// Drive event delivery — used by the happy-path test to fire a fake
// `stock_changed` from inside `act()`.
function emit(eventName: string, payload: unknown): void {
  const bucket = signalrRef.current.handlers.get(eventName);
  if (!bucket) return;
  bucket.forEach((h) => h(payload));
}

vi.mock('./useSignalR', () => {
  // `useSignalR` is both a hook (component subscribes to slices) and a
  // store-shaped function with `getState()`. The component code under
  // test calls both: `useSignalR((s) => s.state)` for re-render binding
  // and `useSignalR.getState().connect()` / `.subscribe(...)` for
  // imperative wiring inside `useEffect`.
  const hook = (selector?: (s: SignalRMock) => unknown) => {
    const m = signalrRef.current;
    if (selector) return selector(m);
    return m;
  };
  hook.getState = () => ({
    state: signalrRef.current.state,
    connect: vi.fn(async () => {
      signalrRef.current.connectCalls += 1;
    }),
    subscribe: (eventName: string, handler: Handler) => {
      signalrRef.current.subscribeCalls.push({ eventName, handler });
      let bucket = signalrRef.current.handlers.get(eventName);
      if (!bucket) {
        bucket = new Set();
        signalrRef.current.handlers.set(eventName, bucket);
      }
      bucket.add(handler);
      const unsub = vi.fn(() => {
        signalrRef.current.handlers.get(eventName)?.delete(handler);
      });
      signalrRef.current.unsubscribeFns.push(unsub);
      return unsub;
    },
  });
  return { useSignalR: hook };
});

// ---------------------------------------------------------------------------
// useAuth mock — controls JWT presence for the connect-on-mount guard.
// ---------------------------------------------------------------------------

const authRef = vi.hoisted(() => {
  return { current: { jwt: null as string | null } };
});

vi.mock('./useAuth', () => {
  const hook = () => authRef.current;
  hook.getState = () => authRef.current;
  return { useAuth: hook };
});

// ---------------------------------------------------------------------------
// inventoryApi mock — keep network out of the unit suite.
// ---------------------------------------------------------------------------

vi.mock('../api/inventory', () => {
  return {
    inventoryApi: {
      listSkus: vi.fn(async () => ({ items: [], page: 1, pageSize: 100, total: 0 })),
      summary: vi.fn(async () => ({
        totalSkus: 0,
        lowStockCount: 0,
        flashSaleCount: 0,
        updatedAt: '2026-05-19T00:00:00Z',
      })),
      ledger: vi.fn(async () => []),
    },
  };
});

// Imports MUST come after vi.mock declarations (vi.mock is hoisted, but
// keeping the order matches the maintainer-readable shape from U7).
import {
  useInventoryQuery,
  useInventorySummaryQuery,
  useSkuLedgerQuery,
} from './useInventoryQuery';

// ---------------------------------------------------------------------------
// Wrapper + helpers
// ---------------------------------------------------------------------------

function makeWrapper() {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
  return { qc, wrapper };
}

const VALID_JWT = 'header.payload.signature';

beforeEach(() => {
  signalrRef.current = makeSignalRMock();
  authRef.current = { jwt: null };
});

afterEach(() => {
  signalrRef.current = makeSignalRMock();
  authRef.current = { jwt: null };
});

// ---------------------------------------------------------------------------
// Scenarios
// ---------------------------------------------------------------------------

describe('useInventoryQuery — SignalR wire-up (Sprint-7 U9)', () => {
  it('subscribes to stock_changed on mount', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('stock_changed');
  });

  it('emit stock_changed → invalidateQueries({ queryKey: ["inventory"] })', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useInventoryQuery(), { wrapper });

    // Sanity — handler is registered before any emit.
    expect(signalrRef.current.subscribeCalls).toHaveLength(1);

    act(() => {
      emit('stock_changed', { sku: 'YN-001', availableToSell: 42 });
    });

    // Broad prefix-match — sweeps skus / summary / ledger together.
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory'] });
  });

  it("hub state 'connected' → refetchInterval disabled (no polling)", () => {
    signalrRef.current.state = 'connected';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    // Probe the resolved observer options — TanStack Query stores the
    // computed `refetchInterval` on the observer attached to the query.
    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['inventory', 'skus', {}] });
    expect(query).toBeDefined();
    const observer = query!.observers[0]!;
    // The observer's options are the resolved hook-call options. When
    // the hub is connected, the hook resolves refetchInterval to `false`.
    expect(observer.options.refetchInterval).toBe(false);
  });

  it("hub state 'disconnected' → polling stays at 2 s (R13 fallback)", () => {
    signalrRef.current.state = 'disconnected';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['inventory', 'skus', {}] });
    expect(query).toBeDefined();
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(2000);
  });

  it("hub state 'idle' → polling enabled (idle is not connected; R13 fallback)", () => {
    signalrRef.current.state = 'idle';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['inventory', 'skus', {}] });
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(2000);
  });

  it("hub state 'reconnecting' → polling enabled (R13 fallback)", () => {
    signalrRef.current.state = 'reconnecting';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['inventory', 'skus', {}] });
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(2000);
  });

  it('mount with JWT → connect() called once', () => {
    authRef.current = { jwt: VALID_JWT };
    const { wrapper } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    expect(signalrRef.current.connectCalls).toBe(1);
  });

  it('mount without JWT → connect() not called', () => {
    expect(authRef.current.jwt).toBeNull();
    const { wrapper } = makeWrapper();
    renderHook(() => useInventoryQuery(), { wrapper });

    expect(signalrRef.current.connectCalls).toBe(0);
  });

  it('unmount runs the unsubscribe fn returned by useSignalR.subscribe', () => {
    const { wrapper } = makeWrapper();
    const { unmount } = renderHook(() => useInventoryQuery(), { wrapper });

    expect(signalrRef.current.unsubscribeFns).toHaveLength(1);
    const unsub = signalrRef.current.unsubscribeFns[0]!;
    expect(unsub).not.toHaveBeenCalled();

    unmount();

    expect(unsub).toHaveBeenCalledTimes(1);
  });

  it('emit stock_changed AFTER unmount → does NOT invalidate (cleanup wired correctly)', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { unmount } = renderHook(() => useInventoryQuery(), { wrapper });

    unmount();

    act(() => {
      emit('stock_changed', { sku: 'YN-001', availableToSell: 1 });
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });
});

describe('useInventorySummaryQuery — SignalR wire-up', () => {
  it('subscribes to stock_changed on mount', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useInventorySummaryQuery(), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('stock_changed');
  });

  it('emit stock_changed → invalidates inventory queries (broad)', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useInventorySummaryQuery(), { wrapper });

    act(() => {
      emit('stock_changed', { sku: 'YN-001' });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory'] });
  });
});

describe('useSkuLedgerQuery — SignalR wire-up', () => {
  it('subscribes to stock_changed on mount even though it does not poll', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useSkuLedgerQuery('YN-001'), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('stock_changed');
  });

  it('emit stock_changed → invalidates inventory queries (ledger refetches via broad prefix)', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useSkuLedgerQuery('YN-001'), { wrapper });

    act(() => {
      emit('stock_changed', { sku: 'YN-001' });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['inventory'] });
  });
});

describe('useInventoryQuery — Sprint-6 behavior preserved (regression)', () => {
  it('hook signature unchanged — accepts optional ListSkusParams', () => {
    const { wrapper } = makeWrapper();
    // No params
    const noParams = renderHook(() => useInventoryQuery(), { wrapper });
    expect(noParams.result.current).toBeDefined();
    // With params
    const withParams = renderHook(
      () => useInventoryQuery({ search: 'YN', pageSize: 50 }),
      { wrapper },
    );
    expect(withParams.result.current).toBeDefined();
  });

  it('summary query takes no params (Sprint-6 KTD5)', () => {
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useInventorySummaryQuery(), { wrapper });
    expect(result.current).toBeDefined();
  });

  it('ledger query stays disabled when sku is null', () => {
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useSkuLedgerQuery(null), { wrapper });
    // `enabled: false` means the queryFn does not fire.
    expect(result.current.fetchStatus).toBe('idle');
  });
});
