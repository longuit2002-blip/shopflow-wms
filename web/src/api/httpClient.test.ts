import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { httpClient, ApiError } from './httpClient';
import { useAuth, __resetAuthForTests } from '../hooks/useAuth';

const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJvd25lckB5ZW5zYW8udm4iLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJ0ZW5hbnRfc2VsbGVyIiwidGVuYW50X3NsdWciOiJ5ZW5zYW9raGFuaGhvYSIsImV4cCI6OTk5OTk5OTk5OX0' +
  '.signature';

// Response bodies can only be read once, so we build a fresh Response per
// fetch call rather than sharing a single instance across mockResolvedValue.
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

describe('httpClient', () => {
  beforeEach(() => {
    __resetAuthForTests();
    useAuth.getState().login(VALID_JWT);
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    __resetAuthForTests();
    vi.unstubAllGlobals();
  });

  it('GET attaches Authorization + X-Tenant-Slug headers when authenticated', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.get('/api/v1/inventory/skus');

    const callArgs = vi.mocked(globalThis.fetch).mock.calls[0];
    const init = callArgs![1] as RequestInit;
    const headers = init.headers as Headers;
    expect(headers.get('Authorization')).toBe(`Bearer ${VALID_JWT}`);
    expect(headers.get('X-Tenant-Slug')).toBe('yensaokhanhhoa');
    expect(headers.get('Idempotency-Key')).toBeNull();
  });

  it('POST auto-generates a unique Idempotency-Key per call', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.post('/api/v1/inventory/adjustments', { skus: ['A'] });
    await httpClient.post('/api/v1/inventory/adjustments', { skus: ['B'] });

    const headers1 = (vi.mocked(globalThis.fetch).mock.calls[0]![1] as RequestInit).headers as Headers;
    const headers2 = (vi.mocked(globalThis.fetch).mock.calls[1]![1] as RequestInit).headers as Headers;

    const k1 = headers1.get('Idempotency-Key');
    const k2 = headers2.get('Idempotency-Key');
    expect(k1).toBeTruthy();
    expect(k2).toBeTruthy();
    expect(k1).not.toBe(k2);
  });

  it('POST honors a caller-supplied idempotencyKey', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ ok: true }));

    await httpClient.post('/api/v1/inventory/adjustments', { skus: ['A'] }, {
      idempotencyKey: 'caller-supplied-key',
    });

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

  it('returns parsed JSON on 2xx', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(okResponse({ items: ['A', 'B'] }));

    const result = await httpClient.get<{ items: string[] }>('/api/v1/inventory/skus');
    expect(result.items).toEqual(['A', 'B']);
  });

  it('on 401, calls logout() and throws ApiError(401)', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(new Response(null, { status: 401 }));

    await expect(httpClient.get('/api/v1/inventory/skus')).rejects.toThrow(ApiError);
    expect(useAuth.getState().isAuthenticated).toBe(false);
    expect(window.localStorage.getItem('shopflow.auth.jwt')).toBeNull();
  });

  it('on non-2xx (not 401), throws ApiError with parsed body', async () => {
    vi.mocked(globalThis.fetch).mockImplementation(errorResponse(500, { title: 'boom' }));

    await expect(httpClient.get('/api/v1/inventory/skus')).rejects.toMatchObject({
      name: 'ApiError',
      status: 500,
      body: { title: 'boom' },
    });
  });
});
