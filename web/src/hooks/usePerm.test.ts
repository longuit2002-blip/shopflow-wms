import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useAuth, __resetAuthForTests } from './useAuth';
import { usePerm, hasPerm } from './usePerm';

// Same VALID_JWT shape Sprint-8 uses but extended with two perm keys so
// the multi-key tests have something to match against.
const PERM_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJwZXJtIjpbImludmVudG9yeS5yZWFkIiwib3V0Ym91bmQub3JkZXJzLnJlYWQiXSwiZXhwIjo5OTk5OTk5OTk5fQ'
  + '.signature';

const NO_PERM_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

function sessionWith(jwt: string) {
  return {
    accessToken: jwt,
    refreshToken: 'opaque',
    accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

describe('usePerm (Sprint-9.5 U5 perm-based UI gating)', () => {
  beforeEach(() => __resetAuthForTests());
  afterEach(() => __resetAuthForTests());

  it('returns true when the single key is in perm[]', () => {
    useAuth.getState().setSession(sessionWith(PERM_JWT));
    const { result } = renderHook(() => usePerm('inventory.read'));
    expect(result.current).toBe(true);
  });

  it('returns false when the single key is NOT in perm[]', () => {
    useAuth.getState().setSession(sessionWith(PERM_JWT));
    const { result } = renderHook(() => usePerm('inventory.adjust'));
    expect(result.current).toBe(false);
  });

  it('multi-key requires ALL keys present (AND semantics)', () => {
    useAuth.getState().setSession(sessionWith(PERM_JWT));
    const { result } = renderHook(() =>
      usePerm('inventory.read', 'outbound.orders.read'),
    );
    expect(result.current).toBe(true);
  });

  it('multi-key returns false when any key is missing', () => {
    useAuth.getState().setSession(sessionWith(PERM_JWT));
    const { result } = renderHook(() => usePerm('inventory.read', 'admin.users.list'));
    expect(result.current).toBe(false);
  });

  it('returns false when user is null (no session)', () => {
    const { result } = renderHook(() => usePerm('inventory.read'));
    expect(result.current).toBe(false);
  });

  it('returns false when perm[] is empty (stale Sprint-8 session)', () => {
    useAuth.getState().setSession(sessionWith(NO_PERM_JWT));
    const { result } = renderHook(() => usePerm('inventory.read'));
    expect(result.current).toBe(false);
  });

  it('returns false when no keys are requested (defensive default)', () => {
    useAuth.getState().setSession(sessionWith(PERM_JWT));
    const { result } = renderHook(() => usePerm());
    expect(result.current).toBe(false);
  });
});

describe('hasPerm (non-hook snapshot variant)', () => {
  beforeEach(() => __resetAuthForTests());
  afterEach(() => __resetAuthForTests());

  it('matches the hook behaviour without a React tree', () => {
    useAuth.getState().setSession(sessionWith(PERM_JWT));
    expect(hasPerm('inventory.read')).toBe(true);
    expect(hasPerm('admin.users.list')).toBe(false);
  });

  it('returns false with no session', () => {
    expect(hasPerm('inventory.read')).toBe(false);
  });
});
