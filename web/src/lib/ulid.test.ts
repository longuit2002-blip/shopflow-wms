import { describe, it, expect } from 'vitest';
import { ulid } from './ulid';

describe('ulid', () => {
  it('produces a 26-character string', () => {
    expect(ulid()).toHaveLength(26);
  });

  it('uses only the Crockford base32 alphabet', () => {
    const value = ulid();
    expect(value).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/);
  });

  it('encodes the timestamp as the first 10 characters', () => {
    const a = ulid(0);
    const b = ulid(0);
    expect(a.slice(0, 10)).toBe(b.slice(0, 10));
    expect(a.slice(0, 10)).toBe('0000000000');
  });

  it('produces distinct values when called in quick succession', () => {
    const values = new Set<string>();
    for (let i = 0; i < 50; i += 1) {
      values.add(ulid());
    }
    expect(values.size).toBe(50);
  });

  it('preserves lexicographic order across millisecond buckets', () => {
    const earlier = ulid(1_000);
    const later = ulid(2_000);
    expect(earlier < later).toBe(true);
  });
});
