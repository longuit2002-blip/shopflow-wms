import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { httpClient, ApiError } from './httpClient';
import { useAuth, __resetAuthForTests, type StoredSession } from '../hooks/useAuth';

/**
 * Sprint-8.5 U2 — restored under the Sprint-8 U11 token-pair contract.
 *
 * The Sprint-6 version of this file (deleted at commit 47412ec) tested
 * the single-JWT shape and the simple 401→logout path. Sprint-8 U11
 * lifted httpClient to access+refresh with a transparent refresh
 * interceptor — this file covers:
 *
 *   - Happy GET / POST / JSON shape + ULID idempotency-key generation
 *   - Caller-supplied idempotency-key passthrough
 *   - 2xx + 204 + non-2xx response shapes
 *   - 401 + refresh succeeds → original request retries with new token,
 *     idempotency-key persists across retry attempts (server-side dedup
 *     stays honest)
 *   - 401 + refresh fails → clearSession + ApiError(401) thrown
 *   - Concurrent 401 burst → inflightRefresh guard fires refresh exactly
 *     once across all parallel rotations
 *   - No refresh token in store → 401 throws immediately (no refresh
 *     attempt)
 */

// Sample JWT — Sprint-8 claim shape: sub (Guid), email, role,
// tenant_slug. exp is a far-future Unix timestamp so isJwtExpired
// returns false.
const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

const ROTATED_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.rotated-signature';

const STORAGE_KEY = 'shopflow.auth.v2';

function freshSession(accessToken = VALID_JWT, refreshToken = 'opaque-refresh-1'): StoredSession {
  return {
    accessToken,
    refreshToken,
    accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

// Build a fresh Response per fetch call — Response bodies can only be
// read once, so reusing a single instance across mockResolvedValue
// breaks the second consumer.
const okResponse =
  (body: unknown, status = 200) =>
  async () =>
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });

const errorResponse =
  (status: number, body: unknown = { error: 'bad' }) =>
  async () =>
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });

const noBodyResponse =
  (status: number) =>
  async () =>
    new Response(null, { status });

describe('httpClient — Sprint-8 token-pair + refresh interceptor', () => {
  beforeEach(() => {
    __resetAuthForTests();
    useAuth.getState().setSession(freshSession());
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    __resetAuthForTests();
    vi.unstubAllGlobals();
  });

  // ───────────── Happy-path header shape ─────────────

  it('GET attaches Authorization + X-Tenant-Slug headers when authenticated', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.get('/api/v1/inventory/skus');

    const init = vi.mocked(globalThis.fetch).mock.calls[0]![1] as RequestInit;
    const headers = init.headers as Headers;
    expect(headers.get('Authorization')).toBe(`Bearer ${VALID_JWT}`);
    expect(headers.get('X-Tenant-Slug')).toBe('yensaokhanhhoa');
    expect(headers.get('Idempotency-Key')).toBeNull();
  });

  it('POST auto-generates a unique Idempotency-Key per call', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.post('/api/v1/inventory/adjustments', { skus: ['A'] });
    await httpClient.post('/api/v1/inventory/adjustments', { skus: ['B'] });

    const k1 = (
      (vi.mocked(globalThis.fetch).mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const k2 = (
      (vi.mocked(globalThis.fetch).mock.calls[1]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    expect(k1).toBeTruthy();
    expect(k2).toBeTruthy();
    expect(k1).not.toBe(k2);
  });

  it('POST honors a caller-supplied idempotencyKey', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.post(
      '/api/v1/inventory/adjustments',
      { skus: ['A'] },
      { idempotencyKey: 'caller-supplied-key' },
    );

    const headers = (vi.mocked(globalThis.fetch).mock.calls[0]![1] as RequestInit).headers as Headers;
    expect(headers.get('Idempotency-Key')).toBe('caller-supplied-key');
  });

  it('JSON-encodes object bodies and sets Content-Type', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.post('/api/v1/inventory/skus', { sku: 'YS-A' });

    const init = vi.mocked(globalThis.fetch).mock.calls[0]![1] as RequestInit;
    const headers = init.headers as Headers;
    expect(headers.get('Content-Type')).toBe('application/json');
    expect(init.body).toBe(JSON.stringify({ sku: 'YS-A' }));
  });

  // ───────────── 2xx / 204 / non-2xx response shape ─────────────

  it('returns parsed JSON on 2xx', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ items: ['A', 'B'] }));

    const result = await httpClient.get<{ items: string[] }>('/api/v1/inventory/skus');
    expect(result.items).toEqual(['A', 'B']);
  });

  it('returns undefined on 204 No Content', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(noBodyResponse(204));

    const result = await httpClient.delete<undefined>('/api/v1/inventory/skus/X');
    expect(result).toBeUndefined();
  });

  it('on non-2xx (not 401), throws ApiError with parsed body', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(errorResponse(500, { title: 'boom' }));

    await expect(httpClient.get('/api/v1/inventory/skus')).rejects.toMatchObject({
      name: 'ApiError',
      status: 500,
      body: { title: 'boom' },
    });
  });

  // ───────────── 401 + refresh interceptor ─────────────

  it('401 + refresh succeeds → retries with new access token and returns success body', async () => {
    const fetchMock = vi.mocked(globalThis.fetch);

    // 1st call: GET /api/v1/inventory/skus → 401.
    // 2nd call: POST /api/auth/refresh → 200 + new tokens.
    // 3rd call: GET /api/v1/inventory/skus (retry) → 200 + body.
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            accessToken: ROTATED_JWT,
            accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
            refreshToken: 'opaque-refresh-2',
            refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ items: ['A'] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

    const result = await httpClient.get<{ items: string[] }>('/api/v1/inventory/skus');

    expect(result).toEqual({ items: ['A'] });
    expect(fetchMock).toHaveBeenCalledTimes(3);
    // The retry (3rd call) carries the rotated access token.
    const retryInit = fetchMock.mock.calls[2]![1] as RequestInit;
    const retryHeaders = retryInit.headers as Headers;
    expect(retryHeaders.get('Authorization')).toBe(`Bearer ${ROTATED_JWT}`);
    // Refresh call (2nd) was POST /api/auth/refresh.
    expect(fetchMock.mock.calls[1]![0]).toBe('/api/auth/refresh');
    // Store updated to the rotated token pair.
    const state = useAuth.getState();
    expect(state.accessToken).toBe(ROTATED_JWT);
    expect(state.refreshToken).toBe('opaque-refresh-2');
  });

  it('401 + refresh succeeds → idempotency-key persists across retry attempts', async () => {
    const fetchMock = vi.mocked(globalThis.fetch);

    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            accessToken: ROTATED_JWT,
            accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
            refreshToken: 'opaque-refresh-2',
            refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ ok: true }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

    await httpClient.post(
      '/api/v1/inventory/adjustments',
      { skus: ['A'] },
      { idempotencyKey: 'caller-supplied-key' },
    );

    // First attempt (call 0) AND retry (call 2) both carry the same key.
    const firstAttemptKey = (
      (fetchMock.mock.calls[0]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    const retryKey = (
      (fetchMock.mock.calls[2]![1] as RequestInit).headers as Headers
    ).get('Idempotency-Key');
    expect(firstAttemptKey).toBe('caller-supplied-key');
    expect(retryKey).toBe('caller-supplied-key');
  });

  it('401 + refresh fails → clearSession + ApiError(401) thrown', async () => {
    const fetchMock = vi.mocked(globalThis.fetch);

    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 401 })); // refresh also 401

    await expect(httpClient.get('/api/v1/inventory/skus')).rejects.toMatchObject({
      name: 'ApiError',
      status: 401,
    });

    expect(useAuth.getState().isAuthenticated).toBe(false);
    expect(useAuth.getState().accessToken).toBeNull();
    expect(useAuth.getState().refreshToken).toBeNull();
    expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('concurrent 401 burst → refresh fires exactly once (inflightRefresh guard)', async () => {
    const fetchMock = vi.mocked(globalThis.fetch);

    // We can't predict exactly how many times the protected endpoint is
    // called (depends on test scheduling), but the refresh endpoint
    // MUST be called exactly once across all concurrent rotations.
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url === '/api/auth/refresh') {
        return new Response(
          JSON.stringify({
            accessToken: ROTATED_JWT,
            accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
            refreshToken: 'opaque-refresh-2',
            refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      // Protected endpoint: 401 on stale token, 200 on rotated.
      const init = fetchMock.mock.calls[fetchMock.mock.calls.length - 1]?.[1] as RequestInit | undefined;
      const auth = (init?.headers as Headers | undefined)?.get('Authorization');
      if (auth === `Bearer ${ROTATED_JWT}`) {
        return new Response(JSON.stringify({ items: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 401 });
    });

    await Promise.all([
      httpClient.get('/api/v1/inventory/skus'),
      httpClient.get('/api/v1/inventory/orders'),
      httpClient.get('/api/v1/inventory/sync-state'),
    ]);

    const refreshCalls = fetchMock.mock.calls.filter(
      (c) => (typeof c[0] === 'string' ? c[0] : (c[0] as URL).toString()) === '/api/auth/refresh',
    );
    expect(refreshCalls).toHaveLength(1);
  });

  it('401 with no refresh token in store → throws immediately, no refresh attempt', async () => {
    // Wipe the refresh token from the store.
    __resetAuthForTests();
    // Re-populate access token only (no refresh).
    useAuth.getState().setSession(freshSession(VALID_JWT, ''));
    // Empty refresh token blocks the refresh path; sanity-check store state.
    expect(useAuth.getState().refreshToken).toBe('');

    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));

    await expect(httpClient.get('/api/v1/inventory/skus')).rejects.toMatchObject({
      name: 'ApiError',
      status: 401,
    });

    // Refresh endpoint was NEVER hit.
    const refreshCalls = fetchMock.mock.calls.filter(
      (c) => (typeof c[0] === 'string' ? c[0] : (c[0] as URL).toString()) === '/api/auth/refresh',
    );
    expect(refreshCalls).toHaveLength(0);
  });
});
