import { createFileRoute, redirect } from '@tanstack/react-router';

/**
 * Sprint-6 placed an `Outbound` ComingSoon stub at `/outbound`. Sprint-7
 * U10 replaces it with the real Orders list at `/orders`. We cannot delete
 * the file from inside this dispatch (no Delete tool available); the
 * sibling `/orders/index.tsx` is the live route and this entry redirects
 * any stale `/outbound` links so users land on the new screen. A
 * follow-up housekeeping commit can drop this file and let the router's
 * file-scan regenerate `routeTree.gen.ts` without the redirect.
 */
export const Route = createFileRoute('/_auth/outbound')({
  beforeLoad: () => {
    throw redirect({ to: '/orders' });
  },
});
