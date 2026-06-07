/**
 * useSignalR tests — Sprint-7 plan U7 (test-first).
 *
 * Mocks `@microsoft/signalr`'s `HubConnectionBuilder` so the test can:
 *   - Record `withUrl` / `withAutomaticReconnect` / `configureLogging` calls.
 *   - Capture the registered event handlers + lifecycle callbacks
 *     (`onreconnecting`, `onreconnected`, `onclose`).
 *   - Drive `start()` / `stop()` resolutions or rejections deterministically.
 *   - Emit fake hub events on demand.
 *
 * The pattern mirrors the Sprint-6 `useInventoryMutations.test.tsx`
 * vi.stubGlobal cadence — controllable stub object surfaced via a
 * module-scope ref so each scenario configures it before mounting.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useAuth, __resetAuthForTests } from './useAuth';

// ---------------------------------------------------------------------------
// @microsoft/signalr mock
// ---------------------------------------------------------------------------

interface MockConnection {
  startCalls: number;
  stopCalls: number;
  startResult: 'resolve' | 'reject-401' | 'reject-other';
  startResolved: boolean;
  handlers: Map<string, Set<(payload: unknown) => void>>;
  onreconnecting: ((err?: unknown) => void) | null;
  onreconnected: ((connectionId?: string) => void) | null;
  onclose: ((err?: unknown) => void) | null;
  accessTokenFactory: (() => string | null | Promise<string | null>) | null;
  url: string | null;
  start: () => Promise<void>;
  stop: () => Promise<void>;
  on: (eventName: string, handler: (payload: unknown) => void) => void;
  off: (eventName: string, handler?: (payload: unknown) => void) => void;
  emit: (eventName: string, payload: unknown) => void;
}

// `vi.hoisted` is the only way to share state between the test file and a
// `vi.mock` factory — `vi.mock` is hoisted above top-level `let` declarations
// so a regular module-scope variable would be in TDZ when the factory runs.
const mockRef = vi.hoisted(() => {
  return { current: null as unknown as MockConnection };
});

function makeMockConnection(): MockConnection {
  const handlers = new Map<string, Set<(payload: unknown) => void>>();
  const m: MockConnection = {
    startCalls: 0,
    stopCalls: 0,
    startResult: 'resolve',
    startResolved: false,
    handlers,
    onreconnecting: null,
    onreconnected: null,
    onclose: null,
    accessTokenFactory: null,
    url: null,
    start: vi.fn(async () => {
      m.startCalls += 1;
      if (m.startResult === 'reject-401') {
        const err = new Error('Unauthorized') as Error & { statusCode?: number };
        err.statusCode = 401;
        throw err;
      }
      if (m.startResult === 'reject-other') {
        throw new Error('boom');
      }
      m.startResolved = true;
    }),
    stop: vi.fn(async () => {
      m.stopCalls += 1;
    }),
    on: (eventName, handler) => {
      let bucket = handlers.get(eventName);
      if (!bucket) {
        bucket = new Set();
        handlers.set(eventName, bucket);
      }
      bucket.add(handler);
    },
    off: (eventName, handler) => {
      if (!handler) {
        handlers.delete(eventName);
        return;
      }
      handlers.get(eventName)?.delete(handler);
    },
    emit: (eventName, payload) => {
      const bucket = handlers.get(eventName);
      if (!bucket) return;
      bucket.forEach((h) => h(payload));
    },
  };
  return m;
}

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl(
      url: string,
      opts?: { accessTokenFactory?: () => string | null | Promise<string | null> },
    ): this {
      mockRef.current.url = url;
      mockRef.current.accessTokenFactory = opts?.accessTokenFactory ?? null;
      return this;
    }
    withAutomaticReconnect(): this {
      return this;
    }
    configureLogging(): this {
      return this;
    }
    build() {
      const conn = mockRef.current;
      return {
        start: conn.start,
        stop: conn.stop,
        on: conn.on.bind(conn),
        off: conn.off.bind(conn),
        onreconnecting(handler: (err?: unknown) => void) {
          conn.onreconnecting = handler;
        },
        onreconnected(handler: (connectionId?: string) => void) {
          conn.onreconnected = handler;
        },
        onclose(handler: (err?: unknown) => void) {
          conn.onclose = handler;
        },
      };
    }
  }
  const LogLevel = { Trace: 0, Debug: 1, Information: 2, Warning: 3, Error: 4, Critical: 5, None: 6 };
  return { HubConnectionBuilder, LogLevel };
});

// Imports that depend on the mock above must come AFTER vi.mock declarations.
// Vitest hoists vi.mock so the order at the source level doesn't matter, but
// keep the comment as a reminder for future maintainers.
import { useSignalR, __resetSignalRForTests } from './useSignalR';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

// Same VALID_JWT as useAuth.test.ts — owner@yensao.vn, tenant_seller,
// tenant_slug=yensaokhanhhoa, exp far in the future.
const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJvd25lckB5ZW5zYW8udm4iLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJ0ZW5hbnRfc2VsbGVyIiwidGVuYW50X3NsdWciOiJ5ZW5zYW9raGFuaGhvYSIsImV4cCI6OTk5OTk5OTk5OX0' +
  '.signature';

// Yield to micro-task queue so awaited promises in connect() settle before
// assertions. Two ticks because the start() promise chain has two awaits.
async function flushMicrotasks(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
}

beforeEach(() => {
  __resetAuthForTests();
  __resetSignalRForTests();
  mockRef.current = makeMockConnection();
});

afterEach(() => {
  __resetAuthForTests();
  __resetSignalRForTests();
});

// ---------------------------------------------------------------------------
// Scenarios
// ---------------------------------------------------------------------------

describe('useSignalR — connection lifecycle', () => {
  it('mount with JWT → start() called; state transitions idle → connecting → connected', async () => {
    useAuth.getState().login(VALID_JWT);

    // Snapshot of state during connect() — we check the connecting state by
    // calling connect() and asserting state synchronously before flushing.
    const states: string[] = [];
    const unsubStore = useSignalR.subscribe((s) => {
      states.push(s.state);
    });

    expect(useSignalR.getState().state).toBe('idle');

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(mockRef.current.startCalls).toBe(1);
    expect(useSignalR.getState().state).toBe('connected');
    expect(states).toContain('connecting');
    expect(states[states.length - 1]).toBe('connected');

    unsubStore();
  });

  it('mount without JWT → connection not started; state stays idle', async () => {
    expect(useAuth.getState().jwt).toBeNull();

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(mockRef.current.startCalls).toBe(0);
    expect(useSignalR.getState().state).toBe('idle');
  });

  it('start() throws → state lands on disconnected', async () => {
    useAuth.getState().login(VALID_JWT);
    mockRef.current.startResult = 'reject-other';

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(mockRef.current.startCalls).toBe(1);
    expect(useSignalR.getState().state).toBe('disconnected');
  });

  it('401 from negotiate → useAuth.logout() called', async () => {
    useAuth.getState().login(VALID_JWT);
    mockRef.current.startResult = 'reject-401';

    // Replace logout with a spy directly in the store so the call site
    // (`useAuth.getState().logout()` inside connect()) hits the spy
    // regardless of how Zustand internally snapshots state objects.
    const logoutSpy = vi.fn();
    useAuth.setState({ logout: logoutSpy });

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(logoutSpy).toHaveBeenCalled();
    expect(useSignalR.getState().state).toBe('disconnected');
  });

  it('reconnection state machine: onreconnecting → reconnecting; onreconnected → connected; onclose → disconnected', async () => {
    useAuth.getState().login(VALID_JWT);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(useSignalR.getState().state).toBe('connected');

    act(() => {
      mockRef.current.onreconnecting?.(new Error('network blip'));
    });
    expect(useSignalR.getState().state).toBe('reconnecting');

    act(() => {
      mockRef.current.onreconnected?.('conn-1');
    });
    expect(useSignalR.getState().state).toBe('connected');

    act(() => {
      mockRef.current.onclose?.();
    });
    expect(useSignalR.getState().state).toBe('disconnected');
  });

  it('disconnect() stops the underlying connection', async () => {
    useAuth.getState().login(VALID_JWT);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    await act(async () => {
      await useSignalR.getState().disconnect();
      await flushMicrotasks();
    });

    expect(mockRef.current.stopCalls).toBe(1);
  });

  it('accessTokenFactory returns the JWT from useAuth at call time', async () => {
    useAuth.getState().login(VALID_JWT);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(mockRef.current.accessTokenFactory).not.toBeNull();
    const token = await mockRef.current.accessTokenFactory!();
    expect(token).toBe(VALID_JWT);
  });

  it('hub URL resolves to /hub against VITE_API_BASE_URL (empty default)', async () => {
    useAuth.getState().login(VALID_JWT);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(mockRef.current.url).toMatch(/\/hub$/);
  });
});

describe('useSignalR — subscribe / unsubscribe', () => {
  it('subscribe → emit fake event → handler called with payload', async () => {
    useAuth.getState().login(VALID_JWT);

    const handler = vi.fn();
    const unsubscribe = useSignalR.getState().subscribe('stock_changed', handler);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    const payload = { sku: 'YN-001', availableToSell: 42 };
    act(() => {
      mockRef.current.emit('stock_changed', payload);
    });

    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith(payload);

    // Cleanup
    unsubscribe();
  });

  it('two subscribers to the same event → both fire', async () => {
    useAuth.getState().login(VALID_JWT);

    const a = vi.fn();
    const b = vi.fn();
    useSignalR.getState().subscribe('stock_changed', a);
    useSignalR.getState().subscribe('stock_changed', b);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    act(() => {
      mockRef.current.emit('stock_changed', { x: 1 });
    });

    expect(a).toHaveBeenCalledTimes(1);
    expect(b).toHaveBeenCalledTimes(1);
  });

  it('subscribe returns unsubscribe fn → after unsubscribe, next event not delivered', async () => {
    useAuth.getState().login(VALID_JWT);

    const handler = vi.fn();
    const unsubscribe = useSignalR.getState().subscribe('stock_changed', handler);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    act(() => {
      mockRef.current.emit('stock_changed', { first: true });
    });
    expect(handler).toHaveBeenCalledTimes(1);

    unsubscribe();

    act(() => {
      mockRef.current.emit('stock_changed', { second: true });
    });
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('subscribe before connect → handler still fires after connect + event', async () => {
    useAuth.getState().login(VALID_JWT);

    const handler = vi.fn();
    useSignalR.getState().subscribe('saga_transitioned', handler);

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    act(() => {
      mockRef.current.emit('saga_transitioned', { orderId: 'ord-1' });
    });

    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith({ orderId: 'ord-1' });
  });

  it('renderHook surfaces the same singleton state across components', async () => {
    useAuth.getState().login(VALID_JWT);

    const { result } = renderHook(() => useSignalR((s) => s.state));
    expect(result.current).toBe('idle');

    await act(async () => {
      await useSignalR.getState().connect();
      await flushMicrotasks();
    });

    expect(result.current).toBe('connected');
  });
});
