/**
 * Idempotency-Key generator hook.
 *
 * Returns a freshly minted ULID per call. Mutations (POST/PUT/DELETE/PATCH)
 * pin one of these via `httpClient`'s automatic injection; explicit
 * components (modals, forms) can also call `useIdempotencyKey().mint()`
 * before submission so the same key is reused across retries until the
 * user explicitly resubmits.
 *
 * Sprint-6 plan U5. STYLING_SPECS §5 specifies "Idempotency key display"
 * (mono, truncate middle with ellipsis at 16 chars in tables; full string
 * in detail drawers + on hover). The body of the key is what this hook
 * produces — no prefix; the audit / drawer surfaces add prefixes if needed.
 */

import { useCallback, useState } from 'react';
import { ulid } from '../lib/ulid';

export interface IdempotencyKeyHandle {
  /** The currently held key. Stable across re-renders until `mint()` is called. */
  value: string;
  /** Replace the current key with a freshly generated ULID. */
  mint: () => string;
}

export function useIdempotencyKey(): IdempotencyKeyHandle {
  const [value, setValue] = useState<string>(() => ulid());
  const mint = useCallback(() => {
    const next = ulid();
    setValue(next);
    return next;
  }, []);
  return { value, mint };
}
