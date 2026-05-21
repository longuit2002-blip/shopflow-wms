/**
 * Typed wrappers over the Sprint-8 + Sprint-9 Auth API surface
 * (/api/auth/*). Sprint-9.5 U5 extends the Sprint-8 shape with:
 *   - `login()` returning a `LoginResult` discriminated union mapping
 *     the Sprint-9 all-nullable LoginResponse + MFA flags into one of
 *     four kinds (success | mfa-challenge | mfa-enrollment | failure).
 *   - 7 new endpoint wrappers exercising the Sprint-9 surface that
 *     U1-U4 backend already ships: forgotPassword, resetPasswordConfirm,
 *     beginEnroll, verifyEnroll, verifyMfa, disableMfa,
 *     regenerateRecoveryCodes.
 *
 * IMPORTANT: httpClient is NOT used for login + refresh + the MFA
 * challenge/enrollment-verify endpoints — those calls MUST NOT carry
 * an Authorization header (the user is by definition not yet
 * fully authenticated). Logout, change-password, and the
 * full-session-gated endpoints DO require the access token and go
 * through the httpClient.
 */

import { httpClient, ApiError } from './httpClient';
import type { MfaMethod } from '../hooks/useAuth';

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  tenantSlug: string;
}

/**
 * Sprint-9 wire-level LoginResponse — every token / expiry field is
 * nullable because the same endpoint serves the three response kinds.
 * `mfaChallengeRequired` / `mfaEnrollmentRequired` discriminate the
 * MFA branches; `intentToken` is the 5-min HMAC carry-token.
 */
export interface LoginResponse {
  accessToken: string | null;
  accessTokenExpiresAt: string | null;
  refreshToken: string | null;
  refreshTokenExpiresAt: string | null;
  role: string | null;
  email: string | null;
  mfaChallengeRequired: boolean;
  mfaEnrollmentRequired: boolean;
  intentToken: string | null;
  mfaMethods: MfaMethod[] | null;
}

export type LoginResult =
  | {
      kind: 'success';
      accessToken: string;
      accessTokenExpiresAt: string;
      refreshToken: string;
      refreshTokenExpiresAt: string;
      role: string;
      email: string;
    }
  | { kind: 'mfa-challenge'; intentToken: string; mfaMethods: readonly MfaMethod[] }
  | { kind: 'mfa-enrollment'; intentToken: string }
  | { kind: 'failure'; status: number; errorCode: string; message: string };

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
  readonly errorCode: string;
  constructor(status: number, errorCode: string, message: string) {
    super(message);
    this.name = 'LoginFailedError';
    this.status = status;
    this.errorCode = errorCode;
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
    let errorCode = `http.${response.status}`;
    try {
      const parsed = await response.json();
      if (parsed && typeof parsed.detail === 'string') detail = parsed.detail;
      else if (parsed && typeof parsed.title === 'string') detail = parsed.title;
      if (parsed && typeof parsed.error_code === 'string') errorCode = parsed.error_code;
      else if (parsed && typeof parsed.errorCode === 'string') errorCode = parsed.errorCode;
    } catch {
      // ignore parse errors; fall back to the default detail/errorCode
    }
    throw new LoginFailedError(response.status, errorCode, detail);
  }

  return (await response.json()) as TResponse;
}

/**
 * Sprint-9.5 — login() returns a `LoginResult` discriminated union the
 * caller (LoginScreen) switches on. Maps the Sprint-9 all-nullable
 * LoginResponse + MFA flags to one of four shapes. Backend 4xx/5xx
 * becomes `{ kind: 'failure', ... }` with the problem-details
 * `error_code` field propagated.
 */
export async function login(request: LoginRequest): Promise<LoginResult> {
  let response: LoginResponse;
  try {
    response = await postUnauthenticated<LoginRequest, LoginResponse>(
      '/api/auth/login',
      request,
    );
  } catch (err) {
    if (err instanceof LoginFailedError) {
      return {
        kind: 'failure',
        status: err.status,
        errorCode: err.errorCode,
        message: err.message,
      };
    }
    return {
      kind: 'failure',
      status: 0,
      errorCode: 'http.network',
      message: err instanceof Error ? err.message : String(err),
    };
  }

  if (response.mfaEnrollmentRequired && response.intentToken) {
    return { kind: 'mfa-enrollment', intentToken: response.intentToken };
  }
  if (response.mfaChallengeRequired && response.intentToken) {
    return {
      kind: 'mfa-challenge',
      intentToken: response.intentToken,
      mfaMethods: response.mfaMethods ?? ['totp'],
    };
  }
  if (
    response.accessToken
    && response.accessTokenExpiresAt
    && response.refreshToken
    && response.refreshTokenExpiresAt
    && response.role
    && response.email
  ) {
    return {
      kind: 'success',
      accessToken: response.accessToken,
      accessTokenExpiresAt: response.accessTokenExpiresAt,
      refreshToken: response.refreshToken,
      refreshTokenExpiresAt: response.refreshTokenExpiresAt,
      role: response.role,
      email: response.email,
    };
  }
  return {
    kind: 'failure',
    status: 200,
    errorCode: 'auth.login_response_malformed',
    message: 'Login response is missing required fields.',
  };
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

// ─── Sprint-9.5 U5 — new Sprint-9 endpoint wrappers ──────────────────────

export interface ForgotPasswordRequest {
  email: string;
  tenantSlug: string;
}

/**
 * R6-disciplined constant-time forgot-password trigger. Always returns
 * the success shape — never leaks whether the email was on file.
 */
export async function forgotPassword(req: ForgotPasswordRequest): Promise<void> {
  await postUnauthenticated<ForgotPasswordRequest, unknown>(
    '/api/auth/forgot-password',
    req,
  );
}

export interface ResetPasswordConfirmRequest {
  token: string;
  newPassword: string;
}

export async function resetPasswordConfirm(
  req: ResetPasswordConfirmRequest,
): Promise<void> {
  await postUnauthenticated<ResetPasswordConfirmRequest, unknown>(
    '/api/auth/reset-password',
    req,
  );
}

export interface MfaEnrollBeginResponse {
  /** SVG payload of the otpauth QR (Cache-Control: no-store per KTD16). */
  qrSvg: string;
  /** Base32 manual-entry secret. */
  manualSecret: string;
  /** Enrollment GUID — Redis-backed for 10 minutes per KTD10. */
  enrollmentId: string;
}

export async function beginEnroll(intentToken: string): Promise<MfaEnrollBeginResponse> {
  const response = await fetch('/api/auth/mfa/enroll/begin', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      Authorization: `Bearer ${intentToken}`,
    },
    body: '{}',
  });
  if (!response.ok) {
    const status = response.status;
    let detail = `HTTP ${status}`;
    let errorCode = `http.${status}`;
    try {
      const parsed = await response.json();
      if (parsed && typeof parsed.detail === 'string') detail = parsed.detail;
      if (parsed && typeof parsed.error_code === 'string') errorCode = parsed.error_code;
    } catch {
      // best-effort
    }
    throw new LoginFailedError(status, errorCode, detail);
  }
  return (await response.json()) as MfaEnrollBeginResponse;
}

export interface MfaEnrollVerifyRequest {
  enrollmentId: string;
  otp: string;
}

export interface MfaEnrollVerifyResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  role: string;
  email: string;
  /** 10 single-use recovery codes — displayed ONCE per OWASP guidance. */
  recoveryCodes: string[];
}

export async function verifyEnroll(
  intentToken: string,
  req: MfaEnrollVerifyRequest,
): Promise<MfaEnrollVerifyResponse> {
  const response = await fetch('/api/auth/mfa/enroll/verify', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      Authorization: `Bearer ${intentToken}`,
    },
    body: JSON.stringify(req),
  });
  if (!response.ok) {
    const status = response.status;
    let detail = `HTTP ${status}`;
    let errorCode = `http.${status}`;
    try {
      const parsed = await response.json();
      if (parsed && typeof parsed.detail === 'string') detail = parsed.detail;
      if (parsed && typeof parsed.error_code === 'string') errorCode = parsed.error_code;
    } catch {
      // best-effort
    }
    throw new LoginFailedError(status, errorCode, detail);
  }
  return (await response.json()) as MfaEnrollVerifyResponse;
}

export interface MfaVerifyRequest {
  /** Either a 6-digit TOTP or an 8-char recovery code. */
  code: string;
  /** Discriminator the backend uses to pick the verification path. */
  method: MfaMethod;
}

export interface MfaVerifyResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  role: string;
  email: string;
}

export async function verifyMfa(
  intentToken: string,
  req: MfaVerifyRequest,
): Promise<MfaVerifyResponse> {
  const response = await fetch('/api/auth/mfa/verify', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      Authorization: `Bearer ${intentToken}`,
    },
    body: JSON.stringify(req),
  });
  if (!response.ok) {
    const status = response.status;
    let detail = `HTTP ${status}`;
    let errorCode = `http.${status}`;
    try {
      const parsed = await response.json();
      if (parsed && typeof parsed.detail === 'string') detail = parsed.detail;
      if (parsed && typeof parsed.error_code === 'string') errorCode = parsed.error_code;
    } catch {
      // best-effort
    }
    throw new LoginFailedError(status, errorCode, detail);
  }
  return (await response.json()) as MfaVerifyResponse;
}

export async function disableMfa(currentPassword: string): Promise<void> {
  await httpClient.post<void>('/api/auth/mfa/disable', { currentPassword });
}

export interface RegenerateRecoveryCodesResponse {
  recoveryCodes: string[];
}

export async function regenerateRecoveryCodes(): Promise<RegenerateRecoveryCodesResponse> {
  return await httpClient.post<RegenerateRecoveryCodesResponse>(
    '/api/auth/mfa/recovery-codes',
    {},
  );
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
