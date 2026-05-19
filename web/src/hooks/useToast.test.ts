import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { useToast, __resetToastsForTests } from './useToast';

beforeEach(() => {
  vi.useFakeTimers();
  __resetToastsForTests();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useToast', () => {
  it('starts with an empty queue', () => {
    expect(useToast.getState().toasts).toEqual([]);
  });

  it('push appends a toast and returns its id', () => {
    const id = useToast.getState().push({ kind: 'success', title: 'Đã lưu' });
    const toasts = useToast.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]!.id).toBe(id);
    expect(toasts[0]!.title).toBe('Đã lưu');
    expect(toasts[0]!.kind).toBe('success');
  });

  it('auto-dismisses success toasts after 4 s', () => {
    useToast.getState().push({ kind: 'success', title: 'OK' });
    expect(useToast.getState().toasts).toHaveLength(1);
    vi.advanceTimersByTime(3999);
    expect(useToast.getState().toasts).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(useToast.getState().toasts).toHaveLength(0);
  });

  it('auto-dismisses error toasts after 8 s', () => {
    useToast.getState().push({ kind: 'error', title: 'Lỗi' });
    vi.advanceTimersByTime(7999);
    expect(useToast.getState().toasts).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(useToast.getState().toasts).toHaveLength(0);
  });

  it('honours a custom durationMs override', () => {
    useToast.getState().push({ kind: 'success', title: 'OK', durationMs: 100 });
    vi.advanceTimersByTime(100);
    expect(useToast.getState().toasts).toHaveLength(0);
  });

  it('durationMs=0 disables auto-dismiss', () => {
    useToast.getState().push({ kind: 'error', title: 'Sticky', durationMs: 0 });
    vi.advanceTimersByTime(60_000);
    expect(useToast.getState().toasts).toHaveLength(1);
  });

  it('dismiss removes the toast and clears its timer', () => {
    const id = useToast.getState().push({ kind: 'success', title: 'OK' });
    useToast.getState().dismiss(id);
    expect(useToast.getState().toasts).toHaveLength(0);
    // advancing past the original 4 s window must not throw or re-insert
    vi.advanceTimersByTime(5000);
    expect(useToast.getState().toasts).toHaveLength(0);
  });

  it('dismiss is a no-op for an unknown id', () => {
    useToast.getState().push({ kind: 'success', title: 'A' });
    useToast.getState().dismiss('unknown-id');
    expect(useToast.getState().toasts).toHaveLength(1);
  });

  it('clear empties the queue and cancels every pending timer', () => {
    useToast.getState().push({ kind: 'success', title: 'A' });
    useToast.getState().push({ kind: 'error', title: 'B' });
    expect(useToast.getState().toasts).toHaveLength(2);
    useToast.getState().clear();
    expect(useToast.getState().toasts).toHaveLength(0);
    vi.advanceTimersByTime(10_000);
    expect(useToast.getState().toasts).toHaveLength(0);
  });

  it('carries idempotencyKey + traceId on error toasts', () => {
    useToast.getState().push({
      kind: 'error',
      title: 'Lỗi',
      idempotencyKey: '01HXYZ',
      traceId: 'trace-abc',
    });
    const t = useToast.getState().toasts[0]!;
    expect(t.idempotencyKey).toBe('01HXYZ');
    expect(t.traceId).toBe('trace-abc');
  });
});
