/**
 * useSignalR — Zustand singleton owning the SignalR hub connection.
 *
 * Sprint-7 plan U7. Mirrors the Sprint-6 `useAuth` / `useToast` pattern:
 * the store is a process-wide singleton (the hub connection is a singleton
 * by nature) and components subscribe to slices via the hook for re-renders.
 *
 * Public surface (idiomatic call sites):
 *
 *   useSignalR.getState().connect()
 *   useSignalR.getState().disconnect()
 *   const unsub = useSignalR.getState().subscribe('stock_changed', handler)
 *   const state = useSignalR((s) => s.state)
 *
 * Subscription model:
 *   - Module-scope `Map<eventName, Set<handler>>`. Multiple subscribers per
 *     event; subscribe returns an unsubscribe function. Idempotent — the
 *     same handler subscribed twice still only fires once.
 *   - The hub is wired with `connection.on(eventName, ...)` for every name
 *     present in the subscriptions map at connect time, plus any name that
 *     is subscribed AFTER connect (we wire it lazily then). The dispatcher
 *     ignores events for which there are no subscribers, so wiring is
 *     append-only and cheap.
 *
 * Auth:
 *   - `connect()` reads `useAuth.getState().jwt`. If null, the store stays
 *     at `state='idle'` — no connection attempt, no error.
 *   - The `accessTokenFactory` passed to the SDK reads the JWT lazily on
 *     every negotiate / reconnect so token rotation works without rebuild.
 *   - On 401 from negotiate (detected via `err.statusCode === 401`), the
 *     store calls `useAuth.getState().logout()` to flush localStorage + bounce
 *     the route guard back to `/login` — same contract as httpClient.
 *
 * State machine:
 *
 *     idle ──connect()──▶ connecting ──start() resolves──▶ connected
 *                            │                                 │
 *                            │                                 ├── onreconnecting ──▶ reconnecting ── onreconnected ──▶ connected
 *                            │                                 │
 *                            └── start() rejects ──▶ disconnected ◀── onclose ───────┘
 *
 * Sprint-7 trade-off: a single hub URL `/hub` is hosted by Outbound.Api;
 * Gateway routes there (decision #1 in the doc-review). The client connects
 * to a single URL and demultiplexes by event name on the server side.
 */

import { create } from 'zustand';
import type { HubConnection } from '@microsoft/signalr';
import { useAuth } from './useAuth';
import { buildConnection, type SignalRState } from '../lib/signalr';

// ---------------------------------------------------------------------------
// Module-scope subscription state
// ---------------------------------------------------------------------------

type Handler = (payload: unknown) => void;

const subscriptions: Map<string, Set<Handler>> = new Map();
const wiredEvents: Set<string> = new Set();
let connection: HubConnection | null = null;

function dispatch(eventName: string, payload: unknown): void {
  const bucket = subscriptions.get(eventName);
  if (!bucket) return;
  bucket.forEach((handler) => {
    try {
      handler(payload);
    } catch {
      // Subscriber threw — swallow so one bad handler doesn't kill the fanout.
    }
  });
}

function wireEvent(conn: HubConnection, eventName: string): void {
  if (wiredEvents.has(eventName)) return;
  conn.on(eventName, (payload: unknown) => dispatch(eventName, payload));
  wiredEvents.add(eventName);
}

function resolveHubUrl(): string {
  // Vite injects `import.meta.env.VITE_API_BASE_URL`; the cast keeps the
  // hook compilable without a `vite-env.d.ts` declaration on this branch.
  const env = (import.meta as unknown as { env?: Record<string, string | undefined> }).env;
  const base = env?.VITE_API_BASE_URL ?? '';
  return `${base}/hub`;
}

function is401(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;
  const e = err as { statusCode?: number; status?: number };
  return e.statusCode === 401 || e.status === 401;
}

// ---------------------------------------------------------------------------
// Zustand store
// ---------------------------------------------------------------------------

export interface SignalRStore {
  state: SignalRState;
  connect: () => Promise<void>;
  disconnect: () => Promise<void>;
  subscribe: (eventName: string, handler: Handler) => () => void;
}

export const useSignalR = create<SignalRStore>((set, get) => ({
  state: 'idle',

  connect: async () => {
    // Already connected / connecting — no-op so accidental double-mount in
    // React strict-mode dev doesn't kick off a second negotiate.
    const current = get().state;
    if (current === 'connecting' || current === 'connected' || current === 'reconnecting') {
      return;
    }

    const jwt = useAuth.getState().jwt;
    if (!jwt) {
      // No JWT yet — stay idle. Caller can re-invoke connect() once the
      // login flow lands a token.
      set({ state: 'idle' });
      return;
    }

    set({ state: 'connecting' });

    const conn = buildConnection({
      url: resolveHubUrl(),
      accessTokenFactory: () => useAuth.getState().jwt,
      onStateChange: (s) => set({ state: s }),
      onEvent: (eventName, payload) => dispatch(eventName, payload),
      eventNames: Array.from(subscriptions.keys()),
    });

    // Mark the names we wired through buildConnection so a later subscribe()
    // to the same name doesn't double-register the on() handler.
    for (const name of subscriptions.keys()) {
      wiredEvents.add(name);
    }

    connection = conn;

    try {
      await conn.start();
      set({ state: 'connected' });
    } catch (err) {
      if (is401(err)) {
        useAuth.getState().logout();
      }
      set({ state: 'disconnected' });
      // Leave `connection` set so a future subscribe() can still wire
      // events for the next connect(). The reset helper clears it for tests.
    }
  },

  disconnect: async () => {
    const conn = connection;
    if (!conn) {
      set({ state: 'disconnected' });
      return;
    }
    try {
      await conn.stop();
    } finally {
      connection = null;
      wiredEvents.clear();
      set({ state: 'disconnected' });
    }
  },

  subscribe: (eventName, handler) => {
    let bucket = subscriptions.get(eventName);
    if (!bucket) {
      bucket = new Set();
      subscriptions.set(eventName, bucket);
    }
    bucket.add(handler);

    // If we're already connected, wire this event on the existing connection
    // so the first emission post-subscribe is delivered.
    if (connection) {
      wireEvent(connection, eventName);
    }

    return () => {
      const b = subscriptions.get(eventName);
      if (!b) return;
      b.delete(handler);
      if (b.size === 0) {
        subscriptions.delete(eventName);
      }
    };
  },
}));

/**
 * Test-only reset. Mirrors `__resetAuthForTests` / `__resetToastsForTests`.
 * The Zustand singleton + module-scope subscription state both outlive
 * Vitest's per-test cleanup, so each test must reset both.
 */
export function __resetSignalRForTests(): void {
  subscriptions.clear();
  wiredEvents.clear();
  connection = null;
  useSignalR.setState({ state: 'idle' });
}
