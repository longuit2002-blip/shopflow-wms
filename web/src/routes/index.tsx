import { createFileRoute, redirect } from '@tanstack/react-router';

/**
 * Root index `/` — redirects to either `/inventory` (authenticated) or
 * `/login`. The router context's `auth` snapshot is the source of truth.
 */
export const Route = createFileRoute('/')({
  beforeLoad: ({ context }) => {
    if (context.auth.isAuthenticated) {
      throw redirect({ to: '/inventory' });
    }
    throw redirect({ to: '/login' });
  },
});
