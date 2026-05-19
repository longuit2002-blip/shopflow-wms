/**
 * Orders route tests — Sprint-7.5 plan U7 URL-state migration.
 *
 * Pins the route's `validateSearch` schema. See
 * `routes/_auth/inventory.test.tsx` for the philosophy — component-level
 * tests cover behaviour; this file isolates the URL → typed-values boundary.
 *
 * Scenarios:
 *   1. Empty raw → all defaults (undefined).
 *   2. Valid raw → coerced typed values; all 5 filter fields round-trip.
 *   3. Unknown sortDir → undefined fallback.
 *   4. Invalid page → undefined fallback.
 *   5. Empty-string fields normalize to undefined.
 */

import { describe, it, expect } from 'vitest';
import { Route } from './index';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const validateSearch = (Route.options as any).validateSearch as (
  raw: Record<string, unknown>,
) => Record<string, unknown>;

describe('orders route — validateSearch schema', () => {
  it('returns all-undefined when URL has no search params', () => {
    const out = validateSearch({});
    expect(out).toEqual({
      status: undefined,
      channel: undefined,
      since: undefined,
      until: undefined,
      search: undefined,
      sort: undefined,
      sortDir: undefined,
      page: undefined,
    });
  });

  it('coerces valid URL params to typed values', () => {
    const out = validateSearch({
      status: 'Reserved',
      channel: 'SHOPEE',
      since: '2026-05-01T00:00:00Z',
      until: '2026-05-19T23:59:59Z',
      search: 'SHOPEE_ORD_001',
      sort: 'createdAt',
      sortDir: 'desc',
      page: '2',
    });
    expect(out.status).toBe('Reserved');
    expect(out.channel).toBe('SHOPEE');
    expect(out.since).toBe('2026-05-01T00:00:00Z');
    expect(out.until).toBe('2026-05-19T23:59:59Z');
    expect(out.search).toBe('SHOPEE_ORD_001');
    expect(out.sort).toBe('createdAt');
    expect(out.sortDir).toBe('desc');
    expect(out.page).toBe(2);
  });

  it('falls back to undefined when sortDir is unknown', () => {
    expect(validateSearch({ sortDir: 'random' }).sortDir).toBeUndefined();
    expect(validateSearch({ sortDir: 'ascending' }).sortDir).toBeUndefined();
  });

  it('accepts both asc and desc sortDir', () => {
    expect(validateSearch({ sortDir: 'asc' }).sortDir).toBe('asc');
    expect(validateSearch({ sortDir: 'desc' }).sortDir).toBe('desc');
  });

  it('falls back to undefined for invalid page values', () => {
    expect(validateSearch({ page: '0' }).page).toBeUndefined();
    expect(validateSearch({ page: 'abc' }).page).toBeUndefined();
    expect(validateSearch({ page: 0 }).page).toBeUndefined();
    expect(validateSearch({ page: -2 }).page).toBeUndefined();
  });

  it('accepts integer page numbers directly', () => {
    expect(validateSearch({ page: 3 }).page).toBe(3);
  });

  it('normalises empty-string fields to undefined (URL-omission rule)', () => {
    const out = validateSearch({
      status: '',
      channel: '',
      since: '',
      until: '',
      search: '',
      sort: '',
    });
    expect(out.status).toBeUndefined();
    expect(out.channel).toBeUndefined();
    expect(out.since).toBeUndefined();
    expect(out.until).toBeUndefined();
    expect(out.search).toBeUndefined();
    expect(out.sort).toBeUndefined();
  });
});
