import { render, screen } from '@testing-library/react';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
  RouterProvider,
  createRouter,
  createRootRoute,
  createRoute,
  createMemoryHistory,
  Outlet,
} from '@tanstack/react-router';
import { Sidebar } from './Sidebar';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';

// JWT with the broad perm[] set the existing Sidebar tests assume
// (Sprint-9.5 U8 gates each nav item on JWT perm[] claim — without an
// authenticated session every gated item is hidden, which would break
// the pre-U8 "renders 10 nav items" assertion).
// Payload claims:
//   sub, email, role, tenant_slug, exp (far future) + perm[]:
//   ["inventory.read", "inbound.pos.read", "outbound.orders.read",
//    "auth.admin.users.list"]
const SIDEBAR_PERM_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJwZXJtIjpbImludmVudG9yeS5yZWFkIiwiaW5ib3VuZC5wb3MucmVhZCIsIm91dGJvdW5kLm9yZGVycy5yZWFkIiwiYXV0aC5hZG1pbi51c2Vycy5saXN0Il0sImV4cCI6OTk5OTk5OTk5OX0'
  + '.signature';

const NO_PERM_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6InBpY2tlckBleGFtcGxlLmNvbSIsInJvbGUiOiJQaWNrZXIiLCJ0ZW5hbnRfc2x1ZyI6InllbnNhb2toYW5oaG9hIiwicGVybSI6W10sImV4cCI6OTk5OTk5OTk5OX0'
  + '.signature';

function sessionFor(jwt: string) {
  return {
    accessToken: jwt,
    refreshToken: 'opaque',
    accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

/**
 * Mount the Sidebar inside a TanStack Router memory router so its `<Link>`
 * children can call `useLocation()` etc. The memory router is configured
 * with a single root component that renders the Sidebar; tests navigate
 * by overriding `initialEntries`.
 */
function renderSidebarAt(pathname: string) {
  const rootRoute = createRootRoute({
    component: () => (
      <>
        <Sidebar />
        <Outlet />
      </>
    ),
  });
  // Register placeholder routes for every path the Sidebar links to so
  // the router can resolve them without `notFoundComponent` noise.
  const childRoutes = [
    '/dashboard',
    '/inventory',
    '/inbound',
    '/orders',
    '/channels',
    '/sync',
    '/settings',
    '/audit',
    '/tenants',
    '/onboarding',
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: () => null }),
  );
  const router = createRouter({
    routeTree: rootRoute.addChildren(childRoutes),
    history: createMemoryHistory({ initialEntries: [pathname] }),
  });
  return render(<RouterProvider router={router} />);
}

describe('Sidebar', () => {
  beforeEach(() => {
    __resetLocaleForTests();
    __resetAuthForTests();
    // Sprint-9.5 U8 — every gated nav item is hidden when the session
    // lacks the relevant perm[] key. Existing pre-U8 tests assume all
    // 10 items render → pre-populate a session with the broad keys.
    useAuth.getState().setSession(sessionFor(SIDEBAR_PERM_JWT));
  });

  afterEach(() => {
    __resetLocaleForTests();
    __resetAuthForTests();
  });

  it('renders 10 nav items including Inventory', async () => {
    renderSidebarAt('/inventory');
    expect(await screen.findByRole('link', { name: /Tồn kho/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Tổng quan/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Nhập hàng/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Đơn hàng/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Kênh bán/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Đồng bộ tồn/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Cài đặt/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Audit log/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Tenants/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Khởi tạo mới/i })).toBeInTheDocument();
  });

  it('marks the active item with aria-current=page based on current URL', async () => {
    renderSidebarAt('/inventory');
    const inventory = await screen.findByRole('link', { name: /Tồn kho/i });
    expect(inventory).toHaveAttribute('aria-current', 'page');

    const dashboard = screen.getByRole('link', { name: /Tổng quan/i });
    expect(dashboard).not.toHaveAttribute('aria-current');
  });

  it('hides the upcoming pill on the active item but shows it on stubs', async () => {
    renderSidebarAt('/inventory');
    const inventory = await screen.findByRole('link', { name: /Tồn kho/i });
    expect(inventory.textContent).not.toMatch(/Sprint|Phase/);

    const dashboard = screen.getByRole('link', { name: /Tổng quan/i });
    expect(dashboard.textContent).toMatch(/Sprint 7/);
  });

  it('renders English labels after locale flip', async () => {
    setLang('en');
    renderSidebarAt('/inventory');
    expect(await screen.findByRole('link', { name: /Inventory/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Dashboard/i })).toBeInTheDocument();
  });

  it('renders the Admin section header', async () => {
    renderSidebarAt('/inventory');
    expect(await screen.findByText(/Quản trị/i)).toBeInTheDocument();
  });

  it('renders the dot-matrix logo + ShopFlow wordmark', async () => {
    renderSidebarAt('/inventory');
    expect(await screen.findByRole('img', { name: /ShopFlow logo/i })).toBeInTheDocument();
    expect(screen.getByText('ShopFlow')).toBeInTheDocument();
  });
});

// ─── Sprint-9.5 U8 — perm-based gating tests ─────────────────────────────
describe('Sidebar — Sprint-9.5 U8 perm[] gating (AE4)', () => {
  beforeEach(() => {
    __resetLocaleForTests();
    __resetAuthForTests();
  });
  afterEach(() => {
    __resetLocaleForTests();
    __resetAuthForTests();
  });

  it('Owner with broad perm[] sees Inventory + Orders + Inbound + Admin', async () => {
    useAuth.getState().setSession(sessionFor(SIDEBAR_PERM_JWT));
    renderSidebarAt('/dashboard');

    expect(await screen.findByRole('link', { name: /Inventory|Tồn kho/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Orders|Đơn hàng/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Inbound|Nhập hàng/i })).toBeInTheDocument();
    // Settings (gated by auth.admin.users.list) visible.
    expect(screen.getByRole('link', { name: /Settings|Cài đặt/i })).toBeInTheDocument();
  });

  it('Picker with empty perm[] sees only ungated items (Dashboard / Channels / Sync)', async () => {
    useAuth.getState().setSession(sessionFor(NO_PERM_JWT));
    renderSidebarAt('/dashboard');

    expect(await screen.findByRole('link', { name: /Dashboard|Tổng quan/i })).toBeInTheDocument();
    // Inventory, Orders, Inbound, Settings all gated → hidden.
    expect(screen.queryByRole('link', { name: /Inventory|Tồn kho/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Orders|Đơn hàng/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Inbound|Nhập hàng/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Settings|Cài đặt/i })).not.toBeInTheDocument();
  });

  it('no session → all gated items hidden', async () => {
    // __resetAuthForTests already cleared the session.
    renderSidebarAt('/dashboard');

    expect(screen.queryByRole('link', { name: /Inventory|Tồn kho/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Orders|Đơn hàng/i })).not.toBeInTheDocument();
    // Dashboard always visible (no permRequired).
    expect(await screen.findByRole('link', { name: /Dashboard|Tổng quan/i })).toBeInTheDocument();
  });
});
