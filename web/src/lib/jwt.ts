/**
 * Tiny JWT payload decoder. NOT a verifier — server signs and verifies;
 * the client decodes only to extract claims it needs to render UI
 * (tenant_slug, role, email, exp).
 *
 * Sprint-6 plan U5. Sprint-7 may switch to a verified server-rendered
 * session cookie; this helper stays as a stop-gap parser either way.
 */

export interface JwtPayload {
  sub?: string;
  email?: string;
  role?: string;
  tenant_slug?: string;
  /**
   * Sprint-9 KTD1 — emitted as a JSON `string[]` (one `Claim("perm", value)`
   * per key, flattened by JsonWebTokenHandler). The client treats anything
   * other than an array of strings as the empty set per KTD12 fail-closed.
   */
  perm?: string[];
  exp?: number;
  iat?: number;
  iss?: string;
  aud?: string;
  [key: string]: unknown;
}

/**
 * Defensive extractor for the `perm[]` claim. Returns an empty array
 * when the payload is missing the claim, when the claim is present but
 * not a string array (e.g. a string-shaped fallback emitted by a future
 * JsonWebTokenHandler quirk), or when individual entries are non-strings.
 * Empty array → `usePerm` returns false per KTD12 fail-closed.
 */
export function permsFrom(payload: JwtPayload): readonly string[] {
  const raw = payload.perm;
  if (!Array.isArray(raw)) return [];
  return raw.filter((entry): entry is string => typeof entry === 'string');
}

function base64UrlDecode(input: string): string {
  // Convert base64url → base64, then atob.
  let s = input.replace(/-/g, '+').replace(/_/g, '/');
  const pad = s.length % 4;
  if (pad === 2) s += '==';
  else if (pad === 3) s += '=';
  else if (pad !== 0) throw new Error('Invalid base64url input.');
  const binary = atob(s);
  // Decode UTF-8 from the binary string atob returns.
  try {
    return decodeURIComponent(
      Array.from(binary)
        .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );
  } catch {
    return binary;
  }
}

/**
 * Decode the payload portion of a JWT. Throws on malformed input.
 */
export function decodeJwt(token: string): JwtPayload {
  const parts = token.split('.');
  if (parts.length !== 3) {
    throw new Error('JWT must have three dot-separated segments.');
  }
  const payload = parts[1];
  if (!payload) {
    throw new Error('JWT payload segment is empty.');
  }
  const json = base64UrlDecode(payload);
  return JSON.parse(json) as JwtPayload;
}

/**
 * Returns true when the token's `exp` claim is in the past (or absent —
 * tokens without an expiry are treated as expired by policy).
 */
export function isJwtExpired(token: string, nowSec: number = Date.now() / 1000): boolean {
  try {
    const { exp } = decodeJwt(token);
    if (typeof exp !== 'number') return true;
    return exp <= nowSec;
  } catch {
    return true;
  }
}
