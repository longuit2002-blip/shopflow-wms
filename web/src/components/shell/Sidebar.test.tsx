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
    '/outbound',
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
  });

  afterEach(() => {
    __resetLocaleForTests();
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
