import { Outlet, createFileRoute, redirect } from '@tanstack/react-router';
import { Sidebar } from '../components/shell/Sidebar';
import { TopBar } from '../components/shell/TopBar';
import { GuidedTour } from '../components/shell/GuidedTour';
import { ToastViewport } from '../components/primitives/Toast';

/**
 * Authenticated layout — wraps every child route in the
 * Sidebar + TopBar shell. The `beforeLoad` guard bounces unauthenticated
 * users to /login before the layout mounts.
 *
 * Tenant + user identity is hardcoded to the demo fixture; Sprint-7 reads
 * from the JWT claims after real auth lands.
 */

const DEMO_TENANT = {
  monogram: 'YK',
  legalName: 'Yến Sào Khánh Hòa Co., Ltd.',
  erc: '0408123456',
  region: 'Khánh Hòa',
  dbName: 'shopflow_yensaokhanhhoa',
};

const DEMO_USER = {
  name: 'Nguyễn Văn A',
  initials: 'NA',
};

export const Route = createFileRoute('/_auth')({
  beforeLoad: ({ context, location }) => {
    if (!context.auth.isAuthenticated) {
      throw redirect({
        to: '/login',
        search: { redirect: location.href },
      });
    }
  },
  component: AuthLayout,
});

function AuthLayout() {
  return (
    <div style={{ display: 'flex', height: '100vh', minHeight: 0 }}>
      <Sidebar />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0, minHeight: 0 }}>
        <TopBar tenant={DEMO_TENANT} user={DEMO_USER} />
        <main
          style={{
            flex: 1,
            display: 'flex',
            flexDirection: 'column',
            minHeight: 0,
            background: 'var(--bg)',
          }}
        >
          <Outlet />
        </main>
      </div>
      <ToastViewport />
      <GuidedTour />
    </div>
  );
}
