import { describe, it, expect } from 'vitest';
import { decodeJwt, isJwtExpired } from './jwt';

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
