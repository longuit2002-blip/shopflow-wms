import { Outlet, createRootRouteWithContext } from '@tanstack/react-router';
import type { RouterAuthContext } from '../router';

/**
 * Root route — top of the route tree. Doesn't render the TopBar/Sidebar
 * shell directly; that responsibility belongs to the `_auth` layout so
 * that the bare `/login` route renders without the shell.
 *
 * Carries the typed `auth` context so child routes' `beforeLoad` hooks
 * can read `context.auth.isAuthenticated` without importing the store.
 */
export const Route = createRootRouteWithContext<{ auth: RouterAuthContext }>()({
  component: RootComponent,
});

function RootComponent() {
  return <Outlet />;
}
