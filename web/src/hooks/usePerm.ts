/**
 * Permission gate hook (Sprint-9.5 U5). Reads the JWT `perm[]` claim
 * the Auth.Api emits (Sprint-9 KTD1) and returns `true` only when EVERY
 * requested key is present.
 *
 * Per KTD12 — fail-closed:
 *   - `useAuth.user` null → false (no session, no permissions).
 *   - `perm` field missing / null / empty array → false (stale Sprint-8
 *     session before perm[] landed; forces a refresh-token rotation).
 *   - Single missing key in the multi-key call → false (every must
 *     match; AND semantics, not OR).
 *
 * Use at affordance-render time:
 *   ```tsx
 *   const canEdit = usePerm('inventory.adjust', 'inventory.read');
 *   {canEdit && <button>Adjust stock</button>}
 *   ```
 *
 * The backend [Authorize] / [Authorize(Roles="Owner")] gating remains
 * authoritative (Sprint-9 server-side) — `usePerm` only controls UX
 * affordances. The Sprint-10+ per-permission `[Authorize(Policy=...)]`
 * migration flips both layers together.
 */

import { useAuth } from './useAuth';

export function usePerm(...keys: string[]): boolean {
  const user = useAuth((s) => s.user);
  if (!user) return false;
  if (user.perm.length === 0) return false;
  if (keys.length === 0) return false;
  return keys.every((k) => user.perm.includes(k));
}

/**
 * Non-hook variant for non-React contexts (e.g. route loaders, network
 * interceptors). Reads the store snapshot directly instead of
 * subscribing.
 */
export function hasPerm(...keys: string[]): boolean {
  const user = useAuth.getState().user;
  if (!user) return false;
  if (user.perm.length === 0) return false;
  if (keys.length === 0) return false;
  return keys.every((k) => user.perm.includes(k));
}
