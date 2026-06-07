import { redirect } from '@tanstack/react-router';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../hooks/useToast';

/**
 * Sprint-9.5 U8 — TanStack Router `beforeLoad` helper that gates a
 * route by one or more JWT `perm[]` keys (KTD12 fail-closed).
 *
 * Behavior:
 *   - No session → redirect to /login (preserves the user's intent
 *     via the standard `redirect` Sprint-8 pattern).
 *   - Session present but missing any required key → raise an error
 *     toast + redirect to /dashboard.
 *   - All keys present → noop, route continues to load.
 *
 * Use like:
 *   ```typescript
 *   export const Route = createFileRoute('/_auth/admin/users')({
 *     beforeLoad: requirePermission('auth.admin.users.list'),
 *     component: AdminUsersRoute,
 *   });
 *   ```
 */
export function requirePermission(...keys: string[]): () => void {
  return () => {
    const { user } = useAuth.getState();
    if (!user) {
      throw redirect({ to: '/login' });
    }
    const perm = user.perm ?? [];
    if (!keys.every((k) => perm.includes(k))) {
      try {
        useToast.getState().push({
          kind: 'error',
          title: 'Permission denied',
          body: 'You do not have permission to view this page.',
        });
      } catch {
        // Toast store is module-scoped; if reset between tests this
        // throws — but we still want the redirect to fire.
      }
      throw redirect({ to: '/dashboard' });
    }
  };
}
