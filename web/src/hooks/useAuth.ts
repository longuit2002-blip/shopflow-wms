/**
 * Auth store + hook (Sprint-8 U11). Zustand singleton backed by
 * localStorage so the session survives page reloads. Token-pair
 * model: access token (~15 min) + refresh token (7d or 30d
 * remember-me) per Sprint-8 plan R11 + R12.
 *
 * On module load: reads the persisted token pair and rehydrates if
 * the access token isn't already expired. (Expired-access-but-valid-
 * refresh paths happen at first httpClient call → 401 → refresh
 * interceptor; the store does NOT pre-emptively refresh on boot.)
 *
 * Logout flow is split:
 *   - `setSession(session)` populates store + persists after login.
 *   - `clearSession()` mutates state only — caller is responsible for
 *     the network logout call + navigation. This keeps the store
 *     router- + http-agnostic and trivially testable.
 *
 * Sprint-9+ swaps localStorage for an httpOnly-cookie session (the
 * dev-mode localStorage approach carries XSS risk).
 */

import { create } from 'zustand';
import { decodeJwt, isJwtExpired, permsFrom, type JwtPayload } from '../lib/jwt';

const STORAGE_KEY = 'shopflow.auth.v2';

/**
 * Sprint-9.5 U5 — three-way auth state machine bridging login → MFA.
 * `signed-out` is the boot default; `full-session` is the post-login
 * steady state (Sprint-8 behaviour preserved); `mfa-challenge` and
 * `mfa-enrollment` carry an in-memory `intentToken` (5-min HMAC) that
 * never persists to localStorage (KTD8).
 */
export type AuthStateKind = 'signed-out' | 'mfa-challenge' | 'mfa-enrollment' | 'full-session';

export type MfaMethod = 'totp' | 'recovery';

export interface AuthUser {
  email: string;
  role: string;
  tenantSlug: string;
  userId: string;
  /** Sprint-9 KTD1 `perm[]` claim, defensively parsed per KTD12. */
  perm: readonly string[];
}

export interface StoredSession {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
}

export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  accessTokenExpiresAt: string | null;
  refreshTokenExpiresAt: string | null;
  user: AuthUser | null;
  isAuthenticated: boolean;
  /** Sprint-9.5 — three-way state machine. Default `signed-out` on boot. */
  authState: AuthStateKind;
  /** In-memory only (never localStorage); 5-min HMAC intent token from backend. */
  intentToken: string | null;
  /** Methods accepted during `mfa-challenge` (e.g. `['totp', 'recovery']`). */
  mfaMethods: readonly MfaMethod[];
  setSession: (session: StoredSession) => void;
  clearSession: () => void;
  updateTokens: (
    accessToken: string,
    accessTokenExpiresAt: string,
    refreshToken: string,
    refreshTokenExpiresAt: string,
  ) => void;
  /** Sprint-9.5 — transition to `mfa-challenge`; stores intent token in memory. */
  setMfaChallenge: (intentToken: string, methods: readonly MfaMethod[]) => void;
  /** Sprint-9.5 — transition to `mfa-enrollment`; stores intent token in memory. */
  setMfaEnrollment: (intentToken: string) => void;
  /** Sprint-9.5 — drop intent token + transition back to `signed-out`. */
  clearIntent: () => void;
  /** Back-compat alias for the Sprint-6 `jwt` getter. */
  jwt: string | null;
  /** Back-compat alias for `clearSession`. */
  logout: () => void;
  /**
   * Back-compat shim — Sprint-6 callers that only have a JWT (e.g.
   * Sprint-7 useSignalR tests) can still mount a session through
   * this. Builds a synthetic StoredSession with empty refresh + the
   * JWT's exp claim as both expiries. New callers should use
   * `setSession` instead.
   */
  login: (jwt: string) => void;
}

function readPersistedSession(): StoredSession | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as StoredSession;
    if (!parsed.accessToken) {
      window.localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    if (isJwtExpired(parsed.accessToken)) {
      // Access expired — keep the session ONLY if the refresh is
      // still alive; the httpClient's 401 path will rotate on first
      // call. Otherwise clear.
      if (parsed.refreshToken && parsed.refreshTokenExpiresAt
          && new Date(parsed.refreshTokenExpiresAt).getTime() > Date.now()) {
        return parsed;
      }
      window.localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

function userFromJwt(jwt: string | null): AuthUser | null {
  if (!jwt) return null;
  let payload: JwtPayload;
  try {
    payload = decodeJwt(jwt);
  } catch {
    return null;
  }
  const email = typeof payload.email === 'string' ? payload.email : '';
  const role = typeof payload.role === 'string' ? payload.role : '';
  const tenantSlug = typeof payload.tenant_slug === 'string' ? payload.tenant_slug : '';
  const userId = typeof payload.sub === 'string' ? payload.sub : '';
  if (!email || !tenantSlug || !userId) return null;
  // Sprint-9.5 — perm[] always present (empty array fallback per KTD12).
  return { email, role, tenantSlug, userId, perm: permsFrom(payload) };
}

function writeStored(session: StoredSession | null): void {
  if (typeof window === 'undefined') return;
  try {
    if (session) {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } else {
      window.localStorage.removeItem(STORAGE_KEY);
    }
  } catch {
    // localStorage may be unavailable (private mode); fall back silently
  }
}

const initialSession = readPersistedSession();
const initialUser = initialSession ? userFromJwt(initialSession.accessToken) : null;

export const useAuth = create<AuthState>((set, get) => ({
  accessToken: initialSession?.accessToken ?? null,
  refreshToken: initialSession?.refreshToken ?? null,
  accessTokenExpiresAt: initialSession?.accessTokenExpiresAt ?? null,
  refreshTokenExpiresAt: initialSession?.refreshTokenExpiresAt ?? null,
  user: initialUser,
  isAuthenticated: initialUser !== null,
  authState: initialUser !== null ? 'full-session' : 'signed-out',
  intentToken: null,
  mfaMethods: [],
  jwt: initialSession?.accessToken ?? null,
  setSession: (session) => {
    const user = userFromJwt(session.accessToken);
    if (!user) {
      writeStored(null);
      set({
        accessToken: null,
        refreshToken: null,
        accessTokenExpiresAt: null,
        refreshTokenExpiresAt: null,
        user: null,
        isAuthenticated: false,
        authState: 'signed-out',
        intentToken: null,
        mfaMethods: [],
        jwt: null,
      });
      return;
    }
    writeStored(session);
    set({
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
      accessTokenExpiresAt: session.accessTokenExpiresAt,
      refreshTokenExpiresAt: session.refreshTokenExpiresAt,
      user,
      isAuthenticated: true,
      authState: 'full-session',
      // Promotion to full-session clears any in-flight MFA intent.
      intentToken: null,
      mfaMethods: [],
      jwt: session.accessToken,
    });
  },
  updateTokens: (accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt) => {
    const user = userFromJwt(accessToken);
    if (!user) {
      get().clearSession();
      return;
    }
    const next: StoredSession = {
      accessToken,
      accessTokenExpiresAt,
      refreshToken,
      refreshTokenExpiresAt,
    };
    writeStored(next);
    set({
      accessToken,
      refreshToken,
      accessTokenExpiresAt,
      refreshTokenExpiresAt,
      user,
      isAuthenticated: true,
      authState: 'full-session',
      intentToken: null,
      mfaMethods: [],
      jwt: accessToken,
    });
  },
  clearSession: () => {
    writeStored(null);
    set({
      accessToken: null,
      refreshToken: null,
      accessTokenExpiresAt: null,
      refreshTokenExpiresAt: null,
      user: null,
      isAuthenticated: false,
      authState: 'signed-out',
      intentToken: null,
      mfaMethods: [],
      jwt: null,
    });
  },
  setMfaChallenge: (intentToken, methods) => {
    // In-memory only — never persist intent tokens (KTD8). Browser
    // reload during this state forces a fresh login (5-min TTL).
    set({
      authState: 'mfa-challenge',
      intentToken,
      mfaMethods: methods,
    });
  },
  setMfaEnrollment: (intentToken) => {
    set({
      authState: 'mfa-enrollment',
      intentToken,
      mfaMethods: [],
    });
  },
  clearIntent: () => {
    set({
      authState: 'signed-out',
      intentToken: null,
      mfaMethods: [],
    });
  },
  logout: () => {
    writeStored(null);
    set({
      accessToken: null,
      refreshToken: null,
      accessTokenExpiresAt: null,
      refreshTokenExpiresAt: null,
      user: null,
      isAuthenticated: false,
      authState: 'signed-out',
      intentToken: null,
      mfaMethods: [],
      jwt: null,
    });
  },
  login: (jwt: string) => {
    // Sprint-6 back-compat shim — synthesize a StoredSession from a
    // bare JWT. Expiries derived from the exp claim; refresh token
    // empty (a Sprint-6-shaped caller has none). Real Sprint-8
    // login flows in LoginScreen call setSession directly.
    let payload: JwtPayload;
    try {
      payload = decodeJwt(jwt);
    } catch {
      get().clearSession();
      return;
    }
    const expSec = typeof payload.exp === 'number' ? payload.exp : 0;
    const expiresAt = expSec > 0 ? new Date(expSec * 1000).toISOString() : new Date(Date.now() + 60 * 60 * 1000).toISOString();
    get().setSession({
      accessToken: jwt,
      refreshToken: '',
      accessTokenExpiresAt: expiresAt,
      refreshTokenExpiresAt: expiresAt,
    });
  },
}));

/**
 * Test-only reset. The Zustand singleton outlives Vitest's render-
 * tree cleanup, so the suite needs an explicit reset to be
 * deterministic.
 */
export function __resetAuthForTests(): void {
  writeStored(null);
  useAuth.setState({
    accessToken: null,
    refreshToken: null,
    accessTokenExpiresAt: null,
    refreshTokenExpiresAt: null,
    user: null,
    isAuthenticated: false,
    authState: 'signed-out',
    intentToken: null,
    mfaMethods: [],
    jwt: null,
  });
}
