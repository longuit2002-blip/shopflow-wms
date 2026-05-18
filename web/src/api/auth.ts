/**
 * Typed wrapper over POST /auth/login.
 *
 * Sprint-6 plan U5. Calls the dev-mode fake login endpoint (U4) through
 * the gateway proxy at /auth/login. Sprint-7 swaps the endpoint contract
 * for refresh-token flow + TOTP verification; the function signature
 * stays the same so callers don't change.
 *
 * IMPORTANT: httpClient is NOT used here. The login call MUST NOT carry
 * an Authorization header (the user is, by definition, not yet
 * authenticated). Use fetch directly with a minimal header set.
 */

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginUser {
  email: string;
  role: string;
  tenantSlug: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresIn: number;
  tokenType: string;
  user: LoginUser;
}

interface RawLoginResponse {
  accessToken: string;
  expiresIn: number;
  tokenType: string;
  user: { email: string; role: string; tenantSlug: string };
}

export class LoginFailedError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = 'LoginFailedError';
    this.status = status;
  }
}

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const response = await fetch('/auth/login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    let detail = `HTTP ${response.status}`;
    try {
      const body = await response.json();
      if (body && typeof body.detail === 'string') detail = body.detail;
      else if (body && typeof body.title === 'string') detail = body.title;
    } catch {
      // ignore parse errors; use the default detail
    }
    throw new LoginFailedError(response.status, detail);
  }

  const raw = (await response.json()) as RawLoginResponse;
  return {
    accessToken: raw.accessToken,
    expiresIn: raw.expiresIn,
    tokenType: raw.tokenType,
    user: raw.user,
  };
}
