import { Outlet, createRootRouteWithContext } from '@tanstack/react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../queryClient';
import type { RouterAuthContext } from '../router';

/**
 * Root route — top of the route tree. Doesn't render the TopBar/Sidebar
 * shell directly; that responsibility belongs to the `_auth` layout so
 * that the bare `/login` route renders without the shell.
 *
 * Wraps the entire tree in the TanStack Query provider so every screen
 * + drawer + modal shares one cache (Sprint-6 U9 polling, U10 drawer
 * fetches, U11/U12 mutations all hit the same client).
 *
 * Carries the typed `auth` context so child routes' `beforeLoad` hooks
 * can read `context.auth.isAuthenticated` without importing the store.
 */
export const Route = createRootRouteWithContext<{ auth: RouterAuthContext }>()({
  component: RootComponent,
});

function RootComponent() {
  return (
    <QueryClientProvider client={queryClient}>
      <Outlet />
    </QueryClientProvider>
  );
}
