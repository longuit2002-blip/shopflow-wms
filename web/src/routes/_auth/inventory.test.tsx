/**
 * Inventory route tests — Sprint-7.5 plan U7 URL-state migration.
 *
 * The component-level + hook-level tests (`useFilterSearchParams.test.ts`,
 * `FilterStrip.test.tsx`, `SkuTable` via a11y smoke, `LedgerDrawer.test.tsx`)
 * cover the dynamic behaviour end-to-end. This file specifically pins the
 * route's `validateSearch` schema — the single typed boundary between
 * URL strings and the hook's typed values. Drift in the validator is the
 * one thing that would silently break deep-link restoration AND fail open
 * to undefined defaults, hiding the issue from runtime tests of the hook.
 *
 * Scenarios:
 *   1. Empty raw search → all defaults (undefined for narrow types).
 *   2. Valid raw search → coerced to typed values.
 *   3. Unknown sort/sortDir → coerced to undefined (fall back to default).
 *   4. Numeric page string → coerced to integer.
 *   5. Invalid page values (zero, negative, NaN, non-numeric) → undefined.
 */

import { describe, it, expect } from 'vitest';
import { Route } from './inventory';

// `validateSearch` is exposed as part of the Route options.
// Cast away the strictly-typed shape for unit-test ergonomics.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const validateSearch = (Route.options as any).validateSearch as (
  raw: Record<string, unknown>,
) => Record<string, unknown>;

describe('inventory route — validateSearch schema', () => {
  it('returns all-undefined when the URL has no search params', () => {
    const out = validateSearch({});
    expect(out).toEqual({
      search: undefined,
      sort: undefined,
      sortDir: undefined,
      page: undefined,
      selected: undefined,
      ledger: undefined,
    });
  });

  it('coerces valid URL params to typed values', () => {
    const out = validateSearch({
      search: 'YEN-001',
      sort: 'available',
      sortDir: 'asc',
      page: '3',
      selected: 'SKU-RED-100',
      ledger: 'cursor-abc',
    });
    expect(out.search).toBe('YEN-001');
    expect(out.sort).toBe('available');
    expect(out.sortDir).toBe('asc');
    expect(out.page).toBe(3);
    expect(out.selected).toBe('SKU-RED-100');
    expect(out.ledger).toBe('cursor-abc');
  });

  it('falls back to undefined when sort column is unknown', () => {
    const out = validateSearch({ sort: 'not-a-real-column' });
    expect(out.sort).toBeUndefined();
  });

  it('falls back to undefined when sortDir is unknown', () => {
    const out = validateSearch({ sortDir: 'random' });
    expect(out.sortDir).toBeUndefined();
  });

  it('falls back to undefined for invalid page values', () => {
    expect(validateSearch({ page: '0' }).page).toBeUndefined();
    expect(validateSearch({ page: '-3' }).page).toBeUndefined();
    expect(validateSearch({ page: 'abc' }).page).toBeUndefined();
    expect(validateSearch({ page: 0 }).page).toBeUndefined();
    expect(validateSearch({ page: -1 }).page).toBeUndefined();
    expect(validateSearch({ page: 1.5 }).page).toBeUndefined();
  });

  it('accepts integer page numbers directly', () => {
    expect(validateSearch({ page: 5 }).page).toBe(5);
    expect(validateSearch({ page: 1 }).page).toBe(1);
  });

  it('falls back to undefined for empty-string search/selected/ledger', () => {
    const out = validateSearch({ search: '', selected: '', ledger: '' });
    expect(out.search).toBeUndefined();
    expect(out.selected).toBeUndefined();
    expect(out.ledger).toBeUndefined();
  });

  it('accepts all 3 sort columns', () => {
    expect(validateSearch({ sort: 'sku' }).sort).toBe('sku');
    expect(validateSearch({ sort: 'available' }).sort).toBe('available');
    expect(validateSearch({ sort: 'reserved' }).sort).toBe('reserved');
  });
});
