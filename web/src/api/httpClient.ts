/**
 * httpClient — typed fetch wrapper.
 *
 * Sprint-6 plan U5. On every call:
 *
 *   1. Reads the JWT + tenant slug from `useAuth.getState()` (Zustand
 *      singleton outside the React render tree so non-component callers
 *      can use it).
 *   2. If a JWT is present, adds `Authorization: Bearer <jwt>` and
 *      `X-Tenant-Slug: <slug>` headers.
 *   3. For mutations (POST / PUT / PATCH / DELETE), generates a fresh
 *      ULID and adds `Idempotency-Key: <ulid>` unless the caller already
 *      supplied one.
 *   4. JSON-encodes the body when provided as a non-string non-FormData.
 *   5. On 2xx with a JSON `Content-Type`, parses and returns `T`. On
 *      2xx no-body, returns `undefined` cast to `T`.
 *   6. On 401, calls `useAuth.getState().logout()` so route guards
 *      immediately bounce to /login. Throws `ApiError(401)`.
 *   7. On any other non-2xx, throws `ApiError(status, body)`.
 *
 * The `baseUrl` default is `''` (relative paths) so Vite's dev proxy
 * forwards `/api/*` + `/auth/*` to the gateway and production builds
 * also use the same-origin path. Override per-call via the `baseUrl`
 * option if a test harness needs an absolute URL.
 */

import { useAuth } from '../hooks/useAuth';
import { ulid } from '../lib/ulid';

// Module-scoped guard so one 401 burst doesn't fan out into N
// parallel refresh calls; concurrent requests all await the same
// in-flight rotation. Without this, the per-tab parallel widgets
// would each trip the grace-window tombstone on every silent refresh.
let inflightRefresh: Promise<boolean> | null = null;

async function attemptRefresh(): Promise<boolean> {
  if (inflightRefresh) return inflightRefresh;

  const state = useAuth.getState();
  const { refreshToken, user } = state;
  if (!refreshToken || !user) {
    return false;
  }

  inflightRefresh = (async () => {
    try {
      // Dynamic import breaks the cyclic reference — api/auth.ts
      // depends on httpClient for non-auth endpoints, but refresh
      // itself uses unauthenticated fetch.
      const { refresh } = await import('./auth');
      const result = await refresh({
        refreshToken,
        userId: user.userId,
        tenantSlug: user.tenantSlug,
      });
      useAuth.getState().updateTokens(
        result.accessToken,
        result.accessTokenExpiresAt,
        result.refreshToken,
        result.refreshTokenExpiresAt,
      );
      return true;
    } catch {
      return false;
    } finally {
      inflightRefresh = null;
    }
  })();

  return inflightRefresh;
}

export interface HttpRequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  /** JSON body (auto-serialized) or pre-serialized string / FormData. */
  body?: unknown;
  /** Extra headers; case-insensitive merge with defaults. */
  headers?: Record<string, string>;
  /** Idempotency key override; auto-generated when omitted for mutations. */
  idempotencyKey?: string;
  /** AbortSignal pass-through. */
  signal?: AbortSignal;
  /** Override the base URL (defaults to relative paths). */
  baseUrl?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, message: string, body?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }

  /**
   * Sprint-9.5 — extract a stable `error_code` (or `errorCode`) from a
   * problem-details JSON body when present, falling back to a generic
   * `http.<status>` token. Used by UI toasts + the F4 403 path.
   */
  get errorCode(): string {
    if (this.body && typeof this.body === 'object') {
      const b = this.body as Record<string, unknown>;
      if (typeof b.error_code === 'string') return b.error_code;
      if (typeof b.errorCode === 'string') return b.errorCode;
    }
    return `http.${this.status}`;
  }
}

const MUTATION_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

function isMutation(method: string): boolean {
  return MUTATION_METHODS.has(method.toUpperCase());
}

function isPlainJsonPayload(body: unknown): boolean {
  if (body == null) return false;
  if (typeof body === 'string') return false;
  if (typeof FormData !== 'undefined' && body instanceof FormData) return false;
  if (typeof Blob !== 'undefined' && body instanceof Blob) return false;
  if (body instanceof URLSearchParams) return false;
  return true;
}

export async function httpRequest<T = unknown>(
  path: string,
  options: HttpRequestOptions = {},
): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase();
  const url = (options.baseUrl ?? '') + path;
  const idempotencyKey = options.idempotencyKey;

  async function runOnce(skipRefresh: boolean): Promise<Response> {
    const headers = new Headers();
    headers.set('Accept', 'application/json');
    for (const [k, v] of Object.entries(options.headers ?? {})) {
      headers.set(k, v);
    }

    const { jwt, user } = useAuth.getState();
    if (jwt) {
      headers.set('Authorization', `Bearer ${jwt}`);
    }
    if (user?.tenantSlug) {
      headers.set('X-Tenant-Slug', user.tenantSlug);
    }

    if (isMutation(method) && !headers.has('Idempotency-Key')) {
      // Same key on retry — keeps the server-side dedup honest if
      // the retry attempts succeed on the same logical request.
      headers.set('Idempotency-Key', idempotencyKey ?? ulid());
    }

    let body: BodyInit | undefined;
    if (options.body != null) {
      if (isPlainJsonPayload(options.body)) {
        body = JSON.stringify(options.body);
        if (!headers.has('Content-Type')) {
          headers.set('Content-Type', 'application/json');
        }
      } else {
        body = options.body as BodyInit;
      }
    }

    const init: RequestInit = { method, headers, body };
    if (options.signal) init.signal = options.signal;

    const response = await fetch(url, init);

    if (response.status === 401 && !skipRefresh) {
      // Sprint-9.5 — KTD9 401 branching reads authState first. Only
      // `full-session` is eligible for transparent refresh + retry;
      // MFA-state 401 means the intent token expired (or was reset)
      // and the user must re-login; signed-out 401 means a route that
      // somehow hit an authenticated endpoint without a session.
      const authState = useAuth.getState().authState;
      if (authState === 'full-session') {
        const refreshed = await attemptRefresh();
        if (refreshed) {
          return runOnce(true);
        }
      }
    }

    return response;
  }

  const response = await runOnce(false);

  if (response.status === 401) {
    // Branch the cleanup path on the authState that triggered the 401.
    // - full-session: refresh attempt above already failed → tear down
    //   the session (chain revoked / refresh expired).
    // - mfa-challenge / mfa-enrollment: drop the intent token; the user
    //   re-logs in to mint a fresh one.
    // - signed-out: nothing to tear down; surface the error.
    const authState = useAuth.getState().authState;
    if (authState === 'full-session') {
      useAuth.getState().clearSession();
    } else if (authState === 'mfa-challenge' || authState === 'mfa-enrollment') {
      useAuth.getState().clearIntent();
    }
    throw new ApiError(401, 'Unauthorized — session ended.');
  }

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    let parsed: unknown = text;
    if (text && response.headers.get('Content-Type')?.includes('json')) {
      try {
        parsed = JSON.parse(text);
      } catch {
        // leave parsed = text
      }
    }
    throw new ApiError(response.status, `Request failed: ${response.status}`, parsed);
  }

  // 204 No Content or empty body → return undefined.
  const contentType = response.headers.get('Content-Type') ?? '';
  if (response.status === 204 || !contentType.includes('json')) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

export const httpClient = {
  get<T = unknown>(path: string, options: Omit<HttpRequestOptions, 'method' | 'body'> = {}) {
    return httpRequest<T>(path, { ...options, method: 'GET' });
  },
  post<T = unknown>(path: string, body?: unknown, options: Omit<HttpRequestOptions, 'method' | 'body'> = {}) {
    return httpRequest<T>(path, { ...options, method: 'POST', body });
  },
  put<T = unknown>(path: string, body?: unknown, options: Omit<HttpRequestOptions, 'method' | 'body'> = {}) {
    return httpRequest<T>(path, { ...options, method: 'PUT', body });
  },
  patch<T = unknown>(path: string, body?: unknown, options: Omit<HttpRequestOptions, 'method' | 'body'> = {}) {
    return httpRequest<T>(path, { ...options, method: 'PATCH', body });
  },
  delete<T = unknown>(path: string, options: Omit<HttpRequestOptions, 'method' | 'body'> = {}) {
    return httpRequest<T>(path, { ...options, method: 'DELETE' });
  },
};
