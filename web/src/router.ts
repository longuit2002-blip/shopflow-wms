/**
 * TanStack Router instance — Sprint-6 plan U6.
 *
 * Creates the router with `auth` context so route `beforeLoad` guards
 * can check authentication without coupling directly to Zustand. Sprint-7
 * can swap the auth source (e.g. server-rendered session) by changing
 * what's passed here, with no route file changes.
 *
 * `routeTree.gen.ts` is regenerated automatically by the
 * TanStackRouterVite plugin whenever a file under `src/routes/` changes.
 */

import { createRouter } from '@tanstack/react-router';
import { routeTree } from './routeTree.gen';
import { useAuth } from './hooks/useAuth';

export interface RouterAuthContext {
  isAuthenticated: boolean;
}

function getAuthSnapshot(): RouterAuthContext {
  return { isAuthenticated: useAuth.getState().isAuthenticated };
}

export const router = createRouter({
  routeTree,
  context: {
    auth: getAuthSnapshot(),
  },
  // Invalidate the auth context on every navigation so beforeLoad reads
  // fresh state. Without this, a stale `isAuthenticated=false` snapshot
  // from boot-time would still bounce a logged-in user to /login.
  defaultPreload: 'intent',
});

// Subscribe to auth state changes and ask the router to re-run all loaders
// so `_auth` guards re-evaluate after login/logout.
useAuth.subscribe((state, prev) => {
  if (state.isAuthenticated !== prev.isAuthenticated) {
    router.update({ context: { auth: getAuthSnapshot() } });
    router.invalidate();
  }
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
