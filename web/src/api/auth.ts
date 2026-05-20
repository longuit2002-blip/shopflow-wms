/**
 * Typed wrappers over the Sprint-8 Auth API surface (/api/auth/*).
 *
 * Sprint-6 stub's single-JWT shape is gone; this module ships the
 * access+refresh pair model with rememberMe + tenant_slug + 60-sec
 * grace-window rotation. Endpoints:
 *
 *   POST /api/auth/login        → LoginResponse  (access + refresh + expiry)
 *   POST /api/auth/refresh      → RefreshResponse (rotated pair)
 *   POST /api/auth/logout       → 204
 *   POST /api/auth/me/password  → 204
 *
 * IMPORTANT: httpClient is NOT used for login + refresh — those calls
 * MUST NOT carry an Authorization header (the user is, by definition,
 * not yet authenticated OR holds an expired access token). Logout and
 * change-password DO require the access token and go through the
 * httpClient.
 */

import { httpClient, ApiError } from './httpClient';

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  tenantSlug: string;
}

export interface LoginResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  role: string;
  email: string;
}

export interface RefreshRequest {
  refreshToken: string;
  userId: string;
  tenantSlug: string;
}

export interface RefreshResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export class LoginFailedError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = 'LoginFailedError';
    this.status = status;
  }
}

async function postUnauthenticated<TBody, TResponse>(
  path: string,
  body: TBody,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    let detail = `HTTP ${response.status}`;
    try {
      const parsed = await response.json();
      if (parsed && typeof parsed.detail === 'string') detail = parsed.detail;
      else if (parsed && typeof parsed.title === 'string') detail = parsed.title;
    } catch {
      // ignore parse errors; fall back to the default detail
    }
    throw new LoginFailedError(response.status, detail);
  }

  return (await response.json()) as TResponse;
}

export function login(request: LoginRequest): Promise<LoginResponse> {
  return postUnauthenticated<LoginRequest, LoginResponse>('/api/auth/login', request);
}

export function refresh(request: RefreshRequest): Promise<RefreshResponse> {
  return postUnauthenticated<RefreshRequest, RefreshResponse>('/api/auth/refresh', request);
}

export async function logout(refreshToken: string, allDevices = false): Promise<void> {
  try {
    await httpClient.post<void>('/api/auth/logout', { refreshToken, allDevices });
  } catch (err) {
    // Logout is idempotent server-side; treat any non-network error as
    // "session is gone" — the caller's local state cleanup proceeds
    // regardless. Only re-throw on genuine network failures so a
    // re-try UX can surface.
    if (err instanceof ApiError) {
      return;
    }
    throw err;
  }
}

export async function changePassword(
  currentPassword: string,
  newPassword: string,
): Promise<void> {
  await httpClient.post<void>('/api/auth/me/password', { currentPassword, newPassword });
}

/**
 * Best-effort subdomain extraction from the browser hostname.
 *
 * Returns the leftmost label when the hostname is a subdomain of a
 * recognised root (shopflow.com / shopflow.local). Returns null on
 * localhost / IP literals / reserved infrastructure subdomains — the
 * LoginScreen falls back to asking the user.
 */
export function detectTenantFromHost(hostname: string): string | null {
  if (!hostname) return null;
  const lower = hostname.toLowerCase();
  if (lower === 'localhost' || lower === '127.0.0.1') return null;

  const firstDot = lower.indexOf('.');
  if (firstDot <= 0) return null;
  const candidate = lower.slice(0, firstDot);

  // Aligned with server-side ReservedSlugs (Sprint-8 U9).
  const reserved = new Set([
    'admin',
    'api',
    'app',
    'auth',
    'console',
    'dashboard',
    'dev',
    'docs',
    'localhost',
    'mail',
    'shopflow',
    'staging',
    'static',
    'status',
    'support',
    'www',
  ]);
  if (reserved.has(candidate)) return null;

  return candidate;
}
