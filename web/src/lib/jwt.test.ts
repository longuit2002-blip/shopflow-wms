import { describe, it, expect } from 'vitest';
import { decodeJwt, isJwtExpired, permsFrom, type JwtPayload } from './jwt';

// Hand-crafted unsigned JWT (alg=none, sig=empty) — purely for client-side decoding.
// Header: { "alg": "HS256", "typ": "JWT" }
// Payload: { "sub": "owner@yensao.vn", "email": "owner@yensao.vn",
//            "role": "tenant_seller", "tenant_slug": "yensaokhanhhoa",
//            "exp": 9999999999 }  // far future
const SAMPLE_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJvd25lckB5ZW5zYW8udm4iLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJ0ZW5hbnRfc2VsbGVyIiwidGVuYW50X3NsdWciOiJ5ZW5zYW9raGFuaGhvYSIsImV4cCI6OTk5OTk5OTk5OX0' +
  '.signature';

describe('decodeJwt', () => {
  it('decodes the payload to a plain object', () => {
    const payload = decodeJwt(SAMPLE_JWT);
    expect(payload.sub).toBe('owner@yensao.vn');
    expect(payload.email).toBe('owner@yensao.vn');
    expect(payload.role).toBe('tenant_seller');
    expect(payload.tenant_slug).toBe('yensaokhanhhoa');
    expect(payload.exp).toBe(9999999999);
  });

  it('throws when the token has fewer than three segments', () => {
    expect(() => decodeJwt('not.a.jwt.fourparts')).toThrow();
    expect(() => decodeJwt('only.two')).toThrow();
  });
});

describe('isJwtExpired', () => {
  it('returns false for a token with a future exp claim', () => {
    expect(isJwtExpired(SAMPLE_JWT)).toBe(false);
  });

  it('returns true when nowSec is past the exp claim', () => {
    expect(isJwtExpired(SAMPLE_JWT, 10_000_000_000)).toBe(true);
  });

  it('returns true for a malformed token', () => {
    expect(isJwtExpired('garbage')).toBe(true);
  });
});

// Sprint-9.5 U5 — KTD12 defensive parse of the `perm[]` claim.
describe('permsFrom (Sprint-9.5 KTD12)', () => {
  it('returns the string[] when perm claim is a clean array', () => {
    const payload = {
      perm: ['inventory.read', 'outbound.orders.read'],
    } as unknown as JwtPayload;
    expect(permsFrom(payload)).toEqual(['inventory.read', 'outbound.orders.read']);
  });

  it('returns [] when perm claim is absent (Sprint-8 legacy session)', () => {
    expect(permsFrom({} as JwtPayload)).toEqual([]);
  });

  it('returns [] when perm claim is a string instead of an array (fail-closed)', () => {
    // Simulated JsonWebTokenHandler quirk — space-delimited string in
    // place of the array. KTD12 forces empty perm → usePerm false →
    // user re-rotates the refresh token to repopulate the claim.
    const payload = {
      perm: 'inventory.read outbound.orders.read',
    } as unknown as JwtPayload;
    expect(permsFrom(payload)).toEqual([]);
  });

  it('filters out non-string entries (defensive against mixed arrays)', () => {
    const payload = {
      perm: ['a', 123, null, 'b'] as unknown as string[],
    } as unknown as JwtPayload;
    expect(permsFrom(payload)).toEqual(['a', 'b']);
  });

  it('returns [] when perm is explicitly null', () => {
    const payload = { perm: null } as unknown as JwtPayload;
    expect(permsFrom(payload)).toEqual([]);
  });
});
