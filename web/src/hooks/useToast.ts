/**
 * useToast — Zustand store for the global toast queue.
 *
 * Sprint-6 plan U11. Toasts surface success / error feedback from any
 * mutation (adjust stock, set threshold, flash-sale toggle, create SKU).
 *
 * Behaviour:
 *   - Two kinds: 'success' (4 s dwell) and 'error' (8 s dwell) per
 *     STYLING_SPECS §7. Callers can override `durationMs` if they need a
 *     different lifetime.
 *   - Error toasts surface `idempotencyKey` + `traceId` so the user can
 *     hand them to the back-office on retry — also per STYLING_SPECS §7.
 *   - Auto-dismiss is owned by the store (single setTimeout per push) so
 *     the renderer stays a pure projection of state. Dismissing manually
 *     before the timer fires clears the timer.
 *
 * The store is intentionally NOT a React hook in the call-from-render
 * sense — `useToast.getState().push(...)` is the idiomatic call site
 * inside mutation callbacks. `<Toast>` itself subscribes via the hook to
 * re-render when the queue mutates.
 */

import { create } from 'zustand';

export type ToastKind = 'success' | 'error';

export interface ToastInput {
  kind: ToastKind;
  title: string;
  body?: string;
  idempotencyKey?: string;
  traceId?: string;
  /** Override the auto-dismiss timer (ms). 0 disables auto-dismiss. */
  durationMs?: number;
}

export interface Toast extends ToastInput {
  id: string;
}

export interface ToastState {
  toasts: Toast[];
  push: (input: ToastInput) => string;
  dismiss: (id: string) => void;
  clear: () => void;
}

const DEFAULT_DURATION_MS: Record<ToastKind, number> = {
  success: 4000,
  error: 8000,
};

const timers = new Map<string, ReturnType<typeof setTimeout>>();
let nextId = 0;

function generateId(): string {
  nextId += 1;
  return `t-${nextId}`;
}

export const useToast = create<ToastState>((set, get) => ({
  toasts: [],
  push: (input) => {
    const id = generateId();
    const toast: Toast = { id, ...input };
    set((s) => ({ toasts: [...s.toasts, toast] }));

    const dwell = input.durationMs ?? DEFAULT_DURATION_MS[input.kind];
    if (dwell > 0) {
      const timer = setTimeout(() => {
        get().dismiss(id);
      }, dwell);
      timers.set(id, timer);
    }
    return id;
  },
  dismiss: (id) => {
    const timer = timers.get(id);
    if (timer) {
      clearTimeout(timer);
      timers.delete(id);
    }
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
  },
  clear: () => {
    timers.forEach((timer) => clearTimeout(timer));
    timers.clear();
    set({ toasts: [] });
  },
}));

/**
 * Test-only reset. Vitest's cleanup runs after each render tree but the
 * Zustand singleton outlives that scope — mirror the useLocale + useAuth
 * resets so the suite stays deterministic.
 */
export function __resetToastsForTests(): void {
  timers.forEach((timer) => clearTimeout(timer));
  timers.clear();
  nextId = 0;
  useToast.setState({ toasts: [] });
}
