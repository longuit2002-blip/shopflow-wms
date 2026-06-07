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
      // Sprint-9.5 — perm[] from JWT, defensively parsed. The Sprint-8
      // fixture JWT has no perm claim → empty array (KTD12 fail-closed).
      perm: [],
    });
    // Sprint-9.5 — authState transitions to full-session on setSession.
    expect(state.authState).toBe('full-session');
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

// ─── Sprint-9.5 U5 — state machine + intent token + perm[] ───────────────
describe('useAuth (Sprint-9.5 state machine + MFA intent + perm[])', () => {
  beforeEach(() => __resetAuthForTests());
  afterEach(() => __resetAuthForTests());

  // JWT carrying perm[] claim — sub, email, role, tenant_slug, exp + perm.
  // Payload: { "sub": "...", "email": "owner@yensao.vn", "role": "Owner",
  //  "tenant_slug": "yensaokhanhhoa", "perm": ["inventory.read", "outbound.orders.read"], "exp": 9999999999 }
  const PERM_JWT =
    'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
    + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJwZXJtIjpbImludmVudG9yeS5yZWFkIiwib3V0Ym91bmQub3JkZXJzLnJlYWQiXSwiZXhwIjo5OTk5OTk5OTk5fQ'
    + '.signature';

  it('boot default authState is signed-out', () => {
    expect(useAuth.getState().authState).toBe('signed-out');
    expect(useAuth.getState().intentToken).toBeNull();
    expect(useAuth.getState().mfaMethods).toEqual([]);
  });

  it('setMfaChallenge transitions to mfa-challenge + stores intent in-memory', () => {
    useAuth.getState().setMfaChallenge('intent-abc', ['totp', 'recovery']);
    const state = useAuth.getState();

    expect(state.authState).toBe('mfa-challenge');
    expect(state.intentToken).toBe('intent-abc');
    expect(state.mfaMethods).toEqual(['totp', 'recovery']);
    // KTD8 — intent token NEVER persisted to localStorage.
    expect(typeof window !== 'undefined' && window.localStorage.getItem('shopflow.auth.v2')).toBeFalsy();
  });

  it('setMfaEnrollment transitions to mfa-enrollment + stores intent in-memory', () => {
    useAuth.getState().setMfaEnrollment('intent-xyz');
    const state = useAuth.getState();

    expect(state.authState).toBe('mfa-enrollment');
    expect(state.intentToken).toBe('intent-xyz');
    expect(state.mfaMethods).toEqual([]);
    expect(typeof window !== 'undefined' && window.localStorage.getItem('shopflow.auth.v2')).toBeFalsy();
  });

  it('clearIntent drops the intent + returns to signed-out', () => {
    useAuth.getState().setMfaChallenge('intent-abc', ['totp']);
    useAuth.getState().clearIntent();
    const state = useAuth.getState();

    expect(state.authState).toBe('signed-out');
    expect(state.intentToken).toBeNull();
    expect(state.mfaMethods).toEqual([]);
  });

  it('setSession after mfa-challenge promotes to full-session and clears intent', () => {
    useAuth.getState().setMfaChallenge('intent-abc', ['totp']);
    useAuth.getState().setSession(freshSession());
    const state = useAuth.getState();

    expect(state.authState).toBe('full-session');
    expect(state.intentToken).toBeNull();
    expect(state.mfaMethods).toEqual([]);
    expect(state.isAuthenticated).toBe(true);
  });

  it('userFromJwt extracts perm[] from the JWT payload', () => {
    useAuth.getState().setSession(freshSession(PERM_JWT));
    const state = useAuth.getState();

    expect(state.user?.perm).toEqual(['inventory.read', 'outbound.orders.read']);
  });

  it('userFromJwt returns perm: [] when the claim is absent (Sprint-8 legacy session)', () => {
    useAuth.getState().setSession(freshSession()); // VALID_JWT has no perm claim
    expect(useAuth.getState().user?.perm).toEqual([]);
  });

  it('clearSession resets authState + intent + perm', () => {
    useAuth.getState().setSession(freshSession(PERM_JWT));
    useAuth.getState().clearSession();
    const state = useAuth.getState();

    expect(state.authState).toBe('signed-out');
    expect(state.intentToken).toBeNull();
    expect(state.user).toBeNull();
  });
});
