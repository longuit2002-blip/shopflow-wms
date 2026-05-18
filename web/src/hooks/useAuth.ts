/**
 * Auth store + hook. Zustand singleton backed by localStorage so the
 * login state survives page reloads (Sprint-6 plan U5).
 *
 * On module load: reads `localStorage.shopflow.auth.jwt` and rehydrates
 * if the token decodes + has not expired. Otherwise starts in the
 * logged-out state.
 *
 * Logout side effects (clearing localStorage, navigating to /login) are
 * the caller's responsibility — `logout()` only mutates state. This keeps
 * the store router-agnostic and trivially testable. The httpClient's 401
 * handler triggers `logout()` and then the route guard re-renders the
 * login screen.
 *
 * Sprint-7 swaps for httpOnly cookie session + refresh token rotation;
 * this localStorage approach is dev-mode only (carries XSS risk).
 */

import { create } from 'zustand';
import { decodeJwt, isJwtExpired, type JwtPayload } from '../lib/jwt';

const STORAGE_KEY = 'shopflow.auth.jwt';

export interface AuthUser {
  email: string;
  role: string;
  tenantSlug: string;
}

export interface AuthState {
  jwt: string | null;
  user: AuthUser | null;
  isAuthenticated: boolean;
  login: (jwt: string) => void;
  logout: () => void;
}

function readPersistedJwt(): string | null {
  if (typeof window === 'undefined') return null;
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    if (!stored) return null;
    if (isJwtExpired(stored)) {
      window.localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return stored;
  } catch {
    return null;
  }
}

function userFromJwt(jwt: string): AuthUser | null {
  let payload: JwtPayload;
  try {
    payload = decodeJwt(jwt);
  } catch {
    return null;
  }
  const email = typeof payload.email === 'string' ? payload.email : (payload.sub as string);
  const role = typeof payload.role === 'string' ? payload.role : 'tenant_seller';
  const tenantSlug = typeof payload.tenant_slug === 'string' ? payload.tenant_slug : '';
  if (!email || !tenantSlug) return null;
  return { email, role, tenantSlug };
}

function writeStored(jwt: string | null): void {
  if (typeof window === 'undefined') return;
  try {
    if (jwt) window.localStorage.setItem(STORAGE_KEY, jwt);
    else window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // localStorage may be unavailable (private mode); fall back silently
  }
}

const initialJwt = readPersistedJwt();
const initialUser = initialJwt ? userFromJwt(initialJwt) : null;

export const useAuth = create<AuthState>((set) => ({
  jwt: initialUser ? initialJwt : null,
  user: initialUser,
  isAuthenticated: initialUser !== null,
  login: (jwt: string) => {
    const user = userFromJwt(jwt);
    if (!user) {
      // Malformed or missing required claims — fail closed.
      writeStored(null);
      set({ jwt: null, user: null, isAuthenticated: false });
      return;
    }
    writeStored(jwt);
    set({ jwt, user, isAuthenticated: true });
  },
  logout: () => {
    writeStored(null);
    set({ jwt: null, user: null, isAuthenticated: false });
  },
}));

/**
 * Test-only reset hook. Mirrors `__resetLocaleForTests` from useLocale —
 * Vitest's cleanup runs after each render tree, but the Zustand singleton
 * outlives it, so the suite needs an explicit reset to be deterministic.
 */
export function __resetAuthForTests(): void {
  writeStored(null);
  useAuth.setState({ jwt: null, user: null, isAuthenticated: false });
}
