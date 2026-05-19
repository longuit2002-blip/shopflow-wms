/**
 * useFilterSearchParams tests — Sprint-7.5 plan U7 / KTD5.
 *
 * Mocks `@tanstack/react-router`'s `useSearch` + `useNavigate` so the hook
 * under test runs in pure isolation (no real Router instance required).
 * Each scenario primes the URL via the mock's `currentSearch` ref, mounts
 * the hook, and asserts on the captured `navigate({ search })` call.
 *
 * Coverage matches the U7 plan list:
 *   - Read returns defaults when URL is empty.
 *   - Read parses URL params correctly per schema (defaults merged with
 *     URL overrides).
 *   - Write updates URL without full reload (navigate called with `search`
 *     fn; no `window.location.reload`).
 *   - Write with `undefined` for a key removes it from URL.
 *   - Write with a default-equal value removes it from URL (omission rule).
 *   - Invalid URL param falls back to default (validator pretends the key
 *     was absent; defaults applied).
 *   - Filter change auto-resets page AND clears ledger cursor.
 *   - Sort change auto-resets page AND clears ledger cursor.
 *   - Back-button semantic: the next navigate call passes `replace: false`
 *     so each filter change pushes a history entry, allowing back-traversal.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

// ── Mock the router primitives the hook depends on ───────────────────────

interface RouterRef {
  /** Latest search shape returned by useSearch. */
  current: Record<string, unknown>;
  /** All `navigate(opts)` calls captured here in arrival order. */
  navigateCalls: Array<{ search: Record<string, unknown>; replace?: boolean }>;
}

const routerRef: RouterRef = {
  current: {},
  navigateCalls: [],
};

vi.mock('@tanstack/react-router', () => {
  return {
    useSearch: () => routerRef.current,
    useNavigate: () => (opts: { search: (() => Record<string, unknown>) | Record<string, unknown>; replace?: boolean }) => {
      const search =
        typeof opts.search === 'function'
          ? (opts.search as () => Record<string, unknown>)()
          : opts.search;
      routerRef.navigateCalls.push({ search, replace: opts.replace });
      // Mimic the real router: the new URL state is what subsequent reads
      // would observe (we don't actually re-render here in the unit test —
      // `act()` callers re-invoke renderHook for the second read).
      routerRef.current = search;
    },
  };
});

// Import must come after vi.mock declarations.
import { useFilterSearchParams } from './useFilterSearchParams';

// ── Test schema (matches the Inventory shape closely enough) ─────────────

interface TestSchema extends Record<string, unknown> {
  filter?: string;
  sort?: string;
  page?: number;
  selected?: string;
  ledger?: string;
}

const DEFAULTS: TestSchema = {
  filter: undefined,
  sort: undefined,
  page: 1,
  selected: undefined,
  ledger: undefined,
};

// ── Setup ────────────────────────────────────────────────────────────────

beforeEach(() => {
  routerRef.current = {};
  routerRef.navigateCalls = [];
});

// ── Scenarios ────────────────────────────────────────────────────────────

describe('useFilterSearchParams — reading', () => {
  it('returns defaults when the URL is empty', () => {
    routerRef.current = {};
    const { result } = renderHook(() => useFilterSearchParams<TestSchema>(DEFAULTS));
    const [values] = result.current;

    expect(values.filter).toBeUndefined();
    expect(values.sort).toBeUndefined();
    expect(values.page).toBe(1);
    expect(values.selected).toBeUndefined();
    expect(values.ledger).toBeUndefined();
  });

  it('parses URL params and merges them over defaults', () => {
    routerRef.current = {
      filter: 'lowStock',
      sort: 'availableAsc',
      page: 3,
      selected: 'SKU-001',
    };
    const { result } = renderHook(() => useFilterSearchParams<TestSchema>(DEFAULTS));
    const [values] = result.current;

    expect(values.filter).toBe('lowStock');
    expect(values.sort).toBe('availableAsc');
    expect(values.page).toBe(3);
    expect(values.selected).toBe('SKU-001');
    // `ledger` absent from URL → falls back to the default (undefined).
    expect(values.ledger).toBeUndefined();
  });

  it('treats a non-object URL shape as empty (defensive fallback)', () => {
    // Simulate the validator returning something pathological (an array).
    // The helper has to keep working — defaults rule.
    routerRef.current = [] as unknown as Record<string, unknown>;
    const { result } = renderHook(() => useFilterSearchParams<TestSchema>(DEFAULTS));
    const [values] = result.current;
    expect(values.page).toBe(1);
    expect(values.filter).toBeUndefined();
  });

  it('invalid URL param (validator coerced to undefined) falls back to default', () => {
    // The route's validateSearch would normally coerce 'foo' on a typed
    // enum field to undefined; we emulate that contract here. The hook
    // must NOT crash and must return the default.
    routerRef.current = { filter: undefined, page: undefined };
    const { result } = renderHook(() => useFilterSearchParams<TestSchema>(DEFAULTS));
    const [values] = result.current;

    expect(values.filter).toBeUndefined();
    expect(values.page).toBe(1);
  });
});

describe('useFilterSearchParams — writing', () => {
  it('write updates URL via navigate(); no page reload triggered', () => {
    const reloadSpy = vi.fn();
    const original = window.location;
    // jsdom's `window.location.reload` is a function; spy on it.
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...original, reload: reloadSpy },
    });

    routerRef.current = {};
    const { result } = renderHook(() => useFilterSearchParams<TestSchema>(DEFAULTS));

    act(() => {
      result.current[1]({ filter: 'lowStock' });
    });

    expect(routerRef.navigateCalls).toHaveLength(1);
    expect(routerRef.navigateCalls[0].search).toEqual({ filter: 'lowStock' });
    expect(routerRef.navigateCalls[0].replace).toBe(false);
    expect(reloadSpy).not.toHaveBeenCalled();

    Object.defineProperty(window, 'location', { configurable: true, value: original });
  });

  it('write with `undefined` for a key removes it from URL (default-omission)', () => {
    routerRef.current = { filter: 'lowStock', selected: 'SKU-001', ledger: 'cursor-abc' };
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        resetOn: ['filter', 'sort'],
        pageKey: 'page',
        ledgerKey: 'ledger',
      }),
    );

    act(() => {
      // Closing the drawer: clear both `selected` and `ledger`.
      result.current[1]({ selected: undefined, ledger: undefined });
    });

    expect(routerRef.navigateCalls).toHaveLength(1);
    expect(routerRef.navigateCalls[0].search).toEqual({ filter: 'lowStock' });
    // Neither `selected` nor `ledger` survived into the URL shape.
    expect('selected' in routerRef.navigateCalls[0].search).toBe(false);
    expect('ledger' in routerRef.navigateCalls[0].search).toBe(false);
  });

  it('write with a default-equal value omits the key from URL', () => {
    routerRef.current = { page: 5 };
    const { result } = renderHook(() => useFilterSearchParams<TestSchema>(DEFAULTS));

    act(() => {
      // Setting page back to its default (1) → should be omitted.
      result.current[1]({ page: 1 });
    });

    expect(routerRef.navigateCalls).toHaveLength(1);
    expect('page' in routerRef.navigateCalls[0].search).toBe(false);
  });

  it('filter change auto-resets page AND clears ledger cursor', () => {
    routerRef.current = {
      filter: 'all',
      page: 3,
      ledger: 'cursor-abc',
      selected: 'SKU-001',
    };
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        resetOn: ['filter', 'sort'],
        pageKey: 'page',
        ledgerKey: 'ledger',
      }),
    );

    act(() => {
      result.current[1]({ filter: 'lowStock' });
    });

    expect(routerRef.navigateCalls).toHaveLength(1);
    const search = routerRef.navigateCalls[0].search;
    expect(search.filter).toBe('lowStock');
    // page reset to default → omitted from URL.
    expect('page' in search).toBe(false);
    // ledger cleared to default (undefined) → omitted.
    expect('ledger' in search).toBe(false);
    // `selected` was NOT a reset-trigger nor was it touched in the patch,
    // so it should survive.
    expect(search.selected).toBe('SKU-001');
  });

  it('sort change auto-resets page AND clears ledger cursor', () => {
    routerRef.current = {
      sort: 'sku',
      page: 4,
      ledger: 'cursor-xyz',
    };
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        resetOn: ['filter', 'sort'],
        pageKey: 'page',
        ledgerKey: 'ledger',
      }),
    );

    act(() => {
      result.current[1]({ sort: 'availableDesc' });
    });

    const search = routerRef.navigateCalls[0].search;
    expect(search.sort).toBe('availableDesc');
    expect('page' in search).toBe(false);
    expect('ledger' in search).toBe(false);
  });

  it('changing a non-reset key (selected) does NOT reset page or ledger', () => {
    routerRef.current = { page: 3, ledger: 'cursor-abc' };
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        resetOn: ['filter', 'sort'],
        pageKey: 'page',
        ledgerKey: 'ledger',
      }),
    );

    act(() => {
      result.current[1]({ selected: 'SKU-NEW' });
    });

    const search = routerRef.navigateCalls[0].search;
    expect(search.selected).toBe('SKU-NEW');
    // page and ledger preserved from current URL.
    expect(search.page).toBe(3);
    expect(search.ledger).toBe('cursor-abc');
  });

  it('writing the same value as already in URL does NOT trigger a reset', () => {
    routerRef.current = { filter: 'lowStock', page: 3, ledger: 'cursor-abc' };
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        resetOn: ['filter', 'sort'],
        pageKey: 'page',
        ledgerKey: 'ledger',
      }),
    );

    act(() => {
      // No actual change; reset rule should not fire.
      result.current[1]({ filter: 'lowStock' });
    });

    const search = routerRef.navigateCalls[0].search;
    expect(search.filter).toBe('lowStock');
    expect(search.page).toBe(3);
    expect(search.ledger).toBe('cursor-abc');
  });

  it('back-button traversal: each write pushes a history entry (replace=false)', () => {
    routerRef.current = {};
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        resetOn: ['filter'],
        pageKey: 'page',
      }),
    );

    act(() => {
      result.current[1]({ filter: 'lowStock' });
    });
    act(() => {
      result.current[1]({ filter: 'flashSale' });
    });

    expect(routerRef.navigateCalls).toHaveLength(2);
    expect(routerRef.navigateCalls[0].replace).toBe(false);
    expect(routerRef.navigateCalls[1].replace).toBe(false);
  });

  it('omits keys whose value equals the default (page=1 absent from URL)', () => {
    routerRef.current = {};
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, { resetOn: ['filter'], pageKey: 'page' }),
    );

    act(() => {
      // Explicitly setting page to the default value should NOT add it.
      result.current[1]({ filter: 'lowStock', page: 1 });
    });

    expect(routerRef.navigateCalls[0].search).toEqual({ filter: 'lowStock' });
  });
});

describe('useFilterSearchParams — narrow options (no pagination/ledger)', () => {
  it('a route without pageKey/ledgerKey still works for filter writes', () => {
    routerRef.current = {};
    const { result } = renderHook(() =>
      useFilterSearchParams<TestSchema>(DEFAULTS, {
        // No resetOn/pageKey/ledgerKey supplied.
      }),
    );

    act(() => {
      result.current[1]({ filter: 'flashSale' });
    });

    expect(routerRef.navigateCalls[0].search).toEqual({ filter: 'flashSale' });
  });
});
