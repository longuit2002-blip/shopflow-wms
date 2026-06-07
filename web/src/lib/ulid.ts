/**
 * Tiny ULID generator — 26-char Crockford-base32 lexicographically sortable
 * identifier. Used as the body of every `Idempotency-Key` header sent by
 * the frontend (Sprint-6 plan U5 — STYLING_SPECS §5 "Idempotency key
 * display").
 *
 * Spec: https://github.com/ulid/spec
 *
 * Format: TIMESTAMP (10 chars, 48-bit ms since epoch) + RANDOM (16 chars,
 * 80 bits). Sortable by creation time within the same millisecond bucket
 * is approximate (random tail), which is fine for client-side request
 * keys — the server only needs them to be unique enough to dedupe retries
 * over a few seconds.
 *
 * Why not pull `ulidx` or `ulid` npm packages: hand-rolled is ~30 lines,
 * has no deps, and Sprint-6 only needs the encode path. If Sprint-7
 * extends to monotonic generation or decoding, swap in the package.
 */

// Crockford base32 alphabet — no I, L, O, U for unambiguous visual reads.
const ENCODING = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
const ENCODING_LEN = ENCODING.length;
const TIME_LEN = 10;
const RANDOM_LEN = 16;

function encodeTime(now: number): string {
  if (now < 0 || now > 281474976710655) {
    throw new RangeError('ULID time must be in [0, 2^48 - 1]');
  }
  let out = '';
  for (let i = TIME_LEN - 1; i >= 0; i -= 1) {
    const mod = now % ENCODING_LEN;
    out = ENCODING[mod] + out;
    now = (now - mod) / ENCODING_LEN;
  }
  return out;
}

function encodeRandom(len: number, randomBytes: Uint8Array): string {
  let out = '';
  let byteIdx = 0;
  for (let i = 0; i < len; i += 1) {
    out += ENCODING[randomBytes[byteIdx]! % ENCODING_LEN];
    byteIdx += 1;
  }
  return out;
}

function getRandomBytes(len: number): Uint8Array {
  const buf = new Uint8Array(len);
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(buf);
    return buf;
  }
  // No secure RNG (very old environment, or some test runners) — fall back
  // to Math.random. ULIDs aren't security tokens; uniqueness is the bar.
  for (let i = 0; i < len; i += 1) {
    buf[i] = Math.floor(Math.random() * 256);
  }
  return buf;
}

/**
 * Generate a new ULID. Defaults to `Date.now()` for the time component;
 * pass an override for deterministic test cases.
 */
export function ulid(now: number = Date.now()): string {
  const random = getRandomBytes(RANDOM_LEN);
  return encodeTime(now) + encodeRandom(RANDOM_LEN, random);
}
