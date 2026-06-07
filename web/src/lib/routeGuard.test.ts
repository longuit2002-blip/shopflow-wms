import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { requirePermission } from './routeGuard';
import { useAuth, __resetAuthForTests } from '../hooks/useAuth';

// JWT carrying perm[] claim with two keys.
const PERM_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJwZXJtIjpbImludmVudG9yeS5yZWFkIiwib3V0Ym91bmQub3JkZXJzLnJlYWQiXSwiZXhwIjo5OTk5OTk5OTk5fQ' +
  '.signature';

function sessionWith(jwt = PERM_JWT) {
  return {
    accessToken: jwt,
    refreshToken: 'opaque',
    accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

describe('requirePermission route guard (Sprint-9.5 U8)', () => {
  beforeEach(() => __resetAuthForTests());
  afterEach(() => __resetAuthForTests());

  it('proceeds (no throw) when user has all required keys', () => {
    useAuth.getState().setSession(sessionWith());
    const guard = requirePermission('inventory.read');
    expect(() => guard()).not.toThrow();
  });

  it('throws redirect when user lacks any key', () => {
    useAuth.getState().setSession(sessionWith());
    const guard = requirePermission('admin.users.list');
    expect(() => guard()).toThrow();
  });

  it('throws redirect when no session at all', () => {
    const guard = requirePermission('inventory.read');
    expect(() => guard()).toThrow();
  });

  it('multi-key AND semantics — all required', () => {
    useAuth.getState().setSession(sessionWith());
    const okGuard = requirePermission('inventory.read', 'outbound.orders.read');
    expect(() => okGuard()).not.toThrow();
    const failGuard = requirePermission('inventory.read', 'admin.users.list');
    expect(() => failGuard()).toThrow();
  });
});
