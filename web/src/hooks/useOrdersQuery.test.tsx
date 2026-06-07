/**
 * useOrdersQuery tests — Sprint-7 plan U8.
 *
 * Verifies the SignalR wire-up + polling fallback for the Orders surface:
 *
 *   - List + KPI hooks subscribe to `saga_transitioned` and broad-
 *     invalidate `['orders']` on any event.
 *   - Detail + transitions hooks narrow-invalidate `['orders', orderId]`
 *     only when the payload's `OrderId` matches the hook's orderId.
 *   - `refetchInterval === false` when hub state is `'connected'`;
 *     `refetchInterval === 2000` for every other state (R13 fallback).
 *   - Detail + transitions hooks DO NOT poll (no fallback) — they're
 *     SignalR-only since the panel opens on demand.
 *   - Mount with JWT → `connect()` is triggered once.
 *
 * Mirrors `useInventoryQuery.test.tsx`'s `vi.hoisted` ref pattern so we
 * can drive the mock from inside `vi.mock` AND read state from assertions.
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

function emit(eventName: string, payload: unknown): void {
  const bucket = signalrRef.current.handlers.get(eventName);
  if (!bucket) return;
  bucket.forEach((h) => h(payload));
}

vi.mock('./useSignalR', () => {
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
// useAuth mock
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
// ordersApi mock — keep network out of the unit suite.
// ---------------------------------------------------------------------------

const apiRef = vi.hoisted(() => {
  return {
    list: vi.fn(),
    kpis: vi.fn(),
    detail: vi.fn(),
    transitions: vi.fn(),
    seed: vi.fn(),
  };
});

vi.mock('../api/orders', () => {
  return {
    ordersApi: {
      list: apiRef.list,
      kpis: apiRef.kpis,
      detail: apiRef.detail,
      transitions: apiRef.transitions,
      seed: apiRef.seed,
    },
  };
});

// Imports MUST come after vi.mock declarations (vi.mock is hoisted; the
// order matches the maintainer-readable shape from U7).
import {
  useOrdersListQuery,
  useOrderKpiQuery,
  useOrderDetailQuery,
  useOrderTransitionsQuery,
} from './useOrdersQuery';

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

function emptyListResponse() {
  return { items: [], totalCount: 0 };
}

function emptyKpiResponse() {
  return {
    activeOrders: 0,
    awaitingPick: 0,
    awaitingShip: 0,
    failedToday: 0,
  };
}

function fakeDetail(id: string) {
  return {
    id: id,
    channelExternalOrderId: `SHOPEE_${id}`,
    channel: 'Shopee',
    shippingProfile: 'standard',
    status: 'Pending',
    currentSagaState: null,
    expectedWeightTotal: null,
    actualWeightTotal: null,
    labelUrl: null,
    trackingNumber: null,
    pickWaveId: null,
    createdAt: '2026-05-19T00:00:00Z',
    updatedAt: null,
    lines: [],
  };
}

beforeEach(() => {
  signalrRef.current = makeSignalRMock();
  authRef.current = { jwt: null };
  apiRef.list.mockReset().mockResolvedValue(emptyListResponse());
  apiRef.kpis.mockReset().mockResolvedValue(emptyKpiResponse());
  apiRef.detail.mockReset().mockImplementation(async (id: string) => fakeDetail(id));
  apiRef.transitions.mockReset().mockResolvedValue([]);
  apiRef.seed.mockReset();
});

afterEach(() => {
  signalrRef.current = makeSignalRMock();
  authRef.current = { jwt: null };
});

// ---------------------------------------------------------------------------
// useOrdersListQuery
// ---------------------------------------------------------------------------

describe('useOrdersListQuery — SignalR wire-up + polling fallback', () => {
  it('passes the filter through to ordersApi.list', () => {
    const { wrapper } = makeWrapper();
    const filter = { status: 'Reserved', take: 25 };
    renderHook(() => useOrdersListQuery(filter), { wrapper });

    expect(apiRef.list).toHaveBeenCalledWith(filter);
  });

  it('subscribes to saga_transitioned on mount', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useOrdersListQuery({}), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('saga_transitioned');
  });

  it('emit saga_transitioned → invalidateQueries({ queryKey: ["orders"] })', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrdersListQuery({}), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);

    act(() => {
      emit('saga_transitioned', {
        orderId: '00000000-0000-0000-0000-000000000001',
      });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders'] });
  });

  it("hub state 'connected' → refetchInterval disabled", () => {
    signalrRef.current.state = 'connected';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useOrdersListQuery({ status: 'Reserved' }), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['orders', 'list', { status: 'Reserved' }] });
    expect(query).toBeDefined();
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(false);
  });

  it("hub state 'disconnected' → polling at 2 s (R13 fallback)", () => {
    signalrRef.current.state = 'disconnected';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useOrdersListQuery({}), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['orders', 'list', {}] });
    expect(query).toBeDefined();
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(2000);
  });

  it("hub state 'idle' → polling enabled (idle is not connected)", () => {
    signalrRef.current.state = 'idle';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useOrdersListQuery({}), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['orders', 'list', {}] });
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(2000);
  });

  it("hub state 'reconnecting' → polling enabled", () => {
    signalrRef.current.state = 'reconnecting';
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useOrdersListQuery({}), { wrapper });

    const cache = qc.getQueryCache();
    const query = cache.find({ queryKey: ['orders', 'list', {}] });
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(2000);
  });

  it('mount with JWT → connect() called once', () => {
    authRef.current = { jwt: VALID_JWT };
    const { wrapper } = makeWrapper();
    renderHook(() => useOrdersListQuery({}), { wrapper });

    expect(signalrRef.current.connectCalls).toBe(1);
  });

  it('mount without JWT → connect() not called', () => {
    expect(authRef.current.jwt).toBeNull();
    const { wrapper } = makeWrapper();
    renderHook(() => useOrdersListQuery({}), { wrapper });

    expect(signalrRef.current.connectCalls).toBe(0);
  });

  it('unmount runs the unsubscribe fn', () => {
    const { wrapper } = makeWrapper();
    const { unmount } = renderHook(() => useOrdersListQuery({}), { wrapper });

    expect(signalrRef.current.unsubscribeFns).toHaveLength(1);
    const unsub = signalrRef.current.unsubscribeFns[0]!;
    expect(unsub).not.toHaveBeenCalled();

    unmount();

    expect(unsub).toHaveBeenCalledTimes(1);
  });

  it('emit saga_transitioned AFTER unmount → no invalidation', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    const { unmount } = renderHook(() => useOrdersListQuery({}), { wrapper });

    unmount();

    act(() => {
      emit('saga_transitioned', { orderId: '00000000-0000-0000-0000-000000000001' });
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// useOrderKpiQuery
// ---------------------------------------------------------------------------

describe('useOrderKpiQuery — SignalR wire-up + polling fallback', () => {
  it('subscribes to saga_transitioned on mount', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useOrderKpiQuery(), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('saga_transitioned');
  });

  it('emit saga_transitioned → broad invalidate ["orders"]', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderKpiQuery(), { wrapper });

    act(() => {
      emit('saga_transitioned', { orderId: '00000000-0000-0000-0000-0000000000aa' });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders'] });
  });

  it("hub 'connected' → refetchInterval disabled; 'disconnected' → 2 s", () => {
    signalrRef.current.state = 'connected';
    const { wrapper: w1, qc: qc1 } = makeWrapper();
    renderHook(() => useOrderKpiQuery(), { wrapper: w1 });
    const obs1 = qc1.getQueryCache().find({ queryKey: ['orders', 'kpi'] })!.observers[0]!;
    expect(obs1.options.refetchInterval).toBe(false);

    signalrRef.current.state = 'disconnected';
    const { wrapper: w2, qc: qc2 } = makeWrapper();
    renderHook(() => useOrderKpiQuery(), { wrapper: w2 });
    const obs2 = qc2.getQueryCache().find({ queryKey: ['orders', 'kpi'] })!.observers[0]!;
    expect(obs2.options.refetchInterval).toBe(2000);
  });
});

// ---------------------------------------------------------------------------
// useOrderDetailQuery — narrow-invalidate semantics
// ---------------------------------------------------------------------------

describe('useOrderDetailQuery — narrow SignalR invalidation', () => {
  const ORDER_A = '00000000-0000-0000-0000-00000000aaaa';
  const ORDER_B = '00000000-0000-0000-0000-00000000bbbb';

  it('subscribes to saga_transitioned on mount', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useOrderDetailQuery(ORDER_A), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('saga_transitioned');
  });

  it('does NOT poll (refetchInterval === false)', () => {
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useOrderDetailQuery(ORDER_A), { wrapper });

    const query = qc
      .getQueryCache()
      .find({ queryKey: ['orders', ORDER_A, 'detail'] });
    expect(query).toBeDefined();
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(false);
  });

  it('emit for matching orderId → invalidates ["orders", orderId]', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderDetailQuery(ORDER_A), { wrapper });

    act(() => {
      emit('saga_transitioned', { orderId: ORDER_A });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders', ORDER_A] });
  });

  it('emit for a DIFFERENT orderId → DOES NOT invalidate', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderDetailQuery(ORDER_A), { wrapper });

    act(() => {
      emit('saga_transitioned', { orderId: ORDER_B });
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });

  it('two open detail caches — emit for ORDER_A only invalidates ORDER_A', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderDetailQuery(ORDER_A), { wrapper });
    renderHook(() => useOrderDetailQuery(ORDER_B), { wrapper });

    // Both subscriptions are wired.
    expect(signalrRef.current.subscribeCalls).toHaveLength(2);

    act(() => {
      emit('saga_transitioned', { orderId: ORDER_A });
    });

    // ORDER_A's narrow key was invalidated; ORDER_B's was not.
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders', ORDER_A] });
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ['orders', ORDER_B] });
  });

  it('malformed payload (no OrderId) → no invalidation', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderDetailQuery(ORDER_A), { wrapper });

    act(() => {
      emit('saga_transitioned', { foo: 'bar' });
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });

  it('empty orderId → query stays disabled (fetchStatus idle)', () => {
    const { wrapper } = makeWrapper();
    const { result } = renderHook(() => useOrderDetailQuery(''), { wrapper });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

// ---------------------------------------------------------------------------
// useOrderTransitionsQuery — same narrow-invalidate behavior as detail
// ---------------------------------------------------------------------------

describe('useOrderTransitionsQuery — narrow SignalR invalidation', () => {
  const ORDER_A = '00000000-0000-0000-0000-00000000cccc';
  const ORDER_B = '00000000-0000-0000-0000-00000000dddd';

  it('subscribes to saga_transitioned on mount', () => {
    const { wrapper } = makeWrapper();
    renderHook(() => useOrderTransitionsQuery(ORDER_A), { wrapper });

    expect(signalrRef.current.subscribeCalls).toHaveLength(1);
    expect(signalrRef.current.subscribeCalls[0]!.eventName).toBe('saga_transitioned');
  });

  it('does NOT poll (refetchInterval === false)', () => {
    const { wrapper, qc } = makeWrapper();
    renderHook(() => useOrderTransitionsQuery(ORDER_A), { wrapper });

    const query = qc
      .getQueryCache()
      .find({ queryKey: ['orders', ORDER_A, 'transitions'] });
    expect(query).toBeDefined();
    const observer = query!.observers[0]!;
    expect(observer.options.refetchInterval).toBe(false);
  });

  it('emit for matching orderId → invalidates ["orders", orderId]', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderTransitionsQuery(ORDER_A), { wrapper });

    act(() => {
      emit('saga_transitioned', { orderId: ORDER_A });
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['orders', ORDER_A] });
  });

  it('emit for a DIFFERENT orderId → DOES NOT invalidate', () => {
    const { wrapper, qc } = makeWrapper();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useOrderTransitionsQuery(ORDER_A), { wrapper });

    act(() => {
      emit('saga_transitioned', { orderId: ORDER_B });
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });
});
