import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { useAuth, __resetAuthForTests } from './useAuth';

// Sample JWT — Sprint-8 claim shape: sub (Guid), email, role,
// tenant_slug. exp is a far-future Unix timestamp so isJwtExpired
// returns false during the test run.
const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

const MALFORMED_JWT = 'not-a-jwt';

function freshSession(jwt: string = VALID_JWT) {
  return {
    accessToken: jwt,
    refreshToken: 'opaque-refresh',
    accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

describe('useAuth (Sprint-8 token-pair store)', () => {
  beforeEach(() => __resetAuthForTests());
  afterEach(() => __resetAuthForTests());

  it('starts logged out', () => {
    const state = useAuth.getState();
    expect(state.isAuthenticated).toBe(false);
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.user).toBeNull();
  });

  it('setSession decodes the JWT and populates user + tokens', () => {
    useAuth.getState().setSession(freshSession());
    const state = useAuth.getState();

    expect(state.isAuthenticated).toBe(true);
    expect(state.accessToken).toBe(VALID_JWT);
    expect(state.refreshToken).toBe('opaque-refresh');
    expect(state.user).toEqual({
      email: 'owner@yensao.vn',
      role: 'Owner',
      tenantSlug: 'yensaokhanhhoa',
      userId: '8f72f516-cc02-4f54-9cc8-be0f3b96c4f3',
    });
  });

  it('setSession with a malformed JWT fails closed (logged out)', () => {
    useAuth.getState().setSession(freshSession(MALFORMED_JWT));
    const state = useAuth.getState();

    expect(state.isAuthenticated).toBe(false);
    expect(state.accessToken).toBeNull();
  });

  it('clearSession nulls every field and stays logged out', () => {
    useAuth.getState().setSession(freshSession());
    expect(useAuth.getState().isAuthenticated).toBe(true);

    useAuth.getState().clearSession();
    const state = useAuth.getState();

    expect(state.isAuthenticated).toBe(false);
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.user).toBeNull();
  });

  it('updateTokens swaps tokens after a refresh rotation', () => {
    useAuth.getState().setSession(freshSession());
    const newAccess = VALID_JWT; // reuse same JWT for the test claims
    const newRefresh = 'rotated-refresh';
    const newAccessExp = new Date(Date.now() + 15 * 60 * 1000).toISOString();
    const newRefreshExp = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();

    useAuth.getState().updateTokens(newAccess, newAccessExp, newRefresh, newRefreshExp);
    const state = useAuth.getState();

    expect(state.accessToken).toBe(newAccess);
    expect(state.refreshToken).toBe(newRefresh);
    expect(state.user?.email).toBe('owner@yensao.vn');
  });

  it('back-compat: state.logout still clears the session', () => {
    useAuth.getState().setSession(freshSession());
    useAuth.getState().logout();
    expect(useAuth.getState().isAuthenticated).toBe(false);
  });

  it('back-compat: state.jwt mirrors accessToken', () => {
    useAuth.getState().setSession(freshSession());
    expect(useAuth.getState().jwt).toBe(VALID_JWT);
  });
});
