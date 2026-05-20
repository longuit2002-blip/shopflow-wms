/**
 * Sidebar — left-rail navigation + System Health placeholder.
 *
 * Ports the design canon `app.jsx` `<Sidebar>` (~line 46). Sprint-6 U6
 * pivots from prop-based active state to TanStack Router's `<Link>` +
 * `useLocation`, which means:
 *
 * - Active state is derived from the current URL pathname, not a prop.
 * - Nav items render as anchor tags via `<Link to=...>` — browser back/
 *   forward + middle-click open-in-new-tab + keyboard Tab → Enter all
 *   work for free.
 * - The Sidebar is no longer router-agnostic; it imports from
 *   @tanstack/react-router. Sprint-7 swap (e.g. React Router) would
 *   touch this file. Trade-off accepted: cleaner code + correct a11y.
 *
 * Role is hardcoded to Owner (Sprint-6's vertical slice) so the
 * Operator-hides + admin section gating from canon are simplified out.
 * Canon items stay visible because Owner sees everything.
 *
 * System Health values are mock-static — real values stream from the
 * `/system/health` SignalR channel in Sprint-7.
 */

import {
  LayoutDashboard,
  Boxes,
  Truck,
  ShoppingBag,
  Plug,
  RefreshCw,
  Settings,
  FileSearch,
  Building2,
  UserPlus,
  LogOut,
  type LucideIcon,
} from 'lucide-react';
import { Link, useLocation, useRouter } from '@tanstack/react-router';
import { Logo } from '../primitives/Logo';
import { Pill } from '../primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';
import { SCREEN_PATHS } from './screenPaths';
import { useAuth } from '../../hooks/useAuth';
import { logout as apiLogout } from '../../api/auth';

export type ScreenId =
  | 'dashboard'
  | 'inventory'
  | 'inbound'
  | 'orders'
  | 'channels'
  | 'sync'
  | 'settings'
  | 'audit'
  | 'tenants'
  | 'onboarding';

interface NavItem {
  id: ScreenId;
  label: string;
  icon: LucideIcon;
  /** "Coming Sprint-X" badge for stubbed routes. Inventory is null. */
  upcoming?: string;
  /** Section header above the next group. */
  groupBefore?: string;
}

export function Sidebar() {
  // Re-subscribe to locale so labels re-render when LocaleSwitcher flips lang.
  useLocale();
  const location = useLocation();

  const items: NavItem[] = [
    {
      id: 'dashboard',
      label: t('Tổng quan', 'Dashboard'),
      icon: LayoutDashboard,
      upcoming: 'Sprint 7',
    },
    { id: 'inventory', label: t('Tồn kho', 'Inventory'), icon: Boxes },
    {
      id: 'inbound',
      label: t('Nhập hàng', 'Inbound'),
      icon: Truck,
      upcoming: 'Sprint 8',
    },
    {
      id: 'orders',
      label: t('Đơn hàng', 'Orders'),
      icon: ShoppingBag,
    },
    {
      id: 'channels',
      label: t('Kênh bán', 'Channels'),
      icon: Plug,
      upcoming: 'Sprint 8',
    },
    {
      id: 'sync',
      label: t('Đồng bộ tồn', 'Stock sync'),
      icon: RefreshCw,
      upcoming: 'Sprint 8',
    },
    {
      id: 'settings',
      label: t('Cài đặt', 'Settings'),
      icon: Settings,
      upcoming: 'Phase 3',
      groupBefore: t('Quản trị', 'Admin'),
    },
    {
      id: 'audit',
      label: t('Audit log', 'Audit log'),
      icon: FileSearch,
      upcoming: 'Phase 3',
    },
    {
      id: 'tenants',
      label: t('Tenants', 'Tenants'),
      icon: Building2,
      upcoming: 'Phase 3',
    },
    {
      id: 'onboarding',
      label: t('Khởi tạo mới', 'Onboard new'),
      icon: UserPlus,
      upcoming: 'Phase 3',
    },
  ];

  return (
    <aside
      style={{
        width: 220,
        background: 'var(--bg-soft)',
        borderRight: '1px solid var(--line)',
        display: 'flex',
        flexDirection: 'column',
        minHeight: 0,
        flex: 'none',
      }}
    >
      <div style={{ padding: '14px 14px 10px', display: 'flex', alignItems: 'center', gap: 9 }}>
        <Logo size={22} />
        <div>
          <div style={{ fontSize: 13.5, fontWeight: 700, letterSpacing: '-0.01em' }}>ShopFlow</div>
          <div
            className="mono"
            style={{
              fontSize: 9.5,
              color: 'var(--ink-3)',
              letterSpacing: '0.05em',
              textTransform: 'uppercase',
            }}
          >
            WMS · v0.9.0
          </div>
        </div>
      </div>

      <div style={{ padding: '4px 8px 0', flex: 1, display: 'flex', flexDirection: 'column' }}>
        <nav
          aria-label={t('Điều hướng chính', 'Main navigation')}
          className="scroll-y"
          style={{ flex: 1 }}
        >
          {items.map((it) => (
            <NavRow
              key={it.id}
              item={it}
              active={location.pathname.startsWith(SCREEN_PATHS[it.id])}
            />
          ))}
        </nav>

        <UserRow />
        <SystemHealth />
      </div>
    </aside>
  );
}

function UserRow() {
  useLocale();
  const router = useRouter();
  const user = useAuth((s) => s.user);
  const refreshToken = useAuth((s) => s.refreshToken);
  const clearSession = useAuth((s) => s.clearSession);

  if (!user) return null;

  async function handleLogout() {
    // Best-effort revoke server-side; clear local state regardless.
    if (refreshToken) {
      try {
        await apiLogout(refreshToken);
      } catch {
        // Swallow — clearing local state is what matters for UX.
      }
    }
    clearSession();
    router.navigate({ to: '/login' });
  }

  return (
    <div
      style={{
        padding: '10px 6px',
        borderTop: '1px solid var(--line)',
        marginTop: 8,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 8,
      }}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          minWidth: 0,
          padding: '0 4px',
        }}
        title={user.email}
      >
        <div
          className="t-sm"
          style={{
            fontWeight: 600,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}
        >
          {user.email}
        </div>
        <div className="mono" style={{ fontSize: 9.5, color: 'var(--ink-3)' }}>
          {user.role.toUpperCase()} · {user.tenantSlug}
        </div>
      </div>
      <button
        type="button"
        onClick={handleLogout}
        aria-label={t('Đăng xuất', 'Sign out')}
        title={t('Đăng xuất', 'Sign out')}
        style={{
          background: 'transparent',
          border: '1px solid var(--line)',
          borderRadius: 'var(--radius-md)',
          padding: 6,
          cursor: 'pointer',
          color: 'var(--ink-2)',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <LogOut size={14} strokeWidth={1.5} aria-hidden />
      </button>
    </div>
  );
}

interface NavRowProps {
  item: NavItem;
  active: boolean;
}

function NavRow({ item, active }: NavRowProps) {
  const { icon: Icon, label, upcoming, groupBefore, id } = item;
  return (
    <>
      {groupBefore && (
        <div
          style={{
            padding: '14px 10px 4px',
            fontSize: 9.5,
            color: 'var(--ink-4)',
            letterSpacing: '0.08em',
            textTransform: 'uppercase',
            fontWeight: 600,
          }}
        >
          {groupBefore}
        </div>
      )}
      <Link
        to={SCREEN_PATHS[id]}
        className={`nav-item ${active ? 'active' : ''}`}
        aria-current={active ? 'page' : undefined}
        style={{
          textDecoration: 'none',
          color: 'inherit',
        }}
      >
        <Icon size={13} strokeWidth={1.5} aria-hidden />
        <span>{label}</span>
        {upcoming && !active && (
          <span
            className="pill"
            style={{ height: 14, fontSize: 9.5, background: 'transparent', marginLeft: 'auto' }}
          >
            {upcoming}
          </span>
        )}
      </Link>
    </>
  );
}

function SystemHealth() {
  return (
    <div
      style={{
        padding: '10px 6px 12px',
        borderTop: '1px solid var(--line)',
        marginTop: 8,
      }}
    >
      <div className="lbl" style={{ marginBottom: 6, padding: '0 6px' }}>
        {t('Sức khoẻ hệ thống · tenant này', 'System health · this tenant')}
      </div>
      <div style={{ padding: '0 6px', display: 'flex', flexDirection: 'column', gap: 4 }}>
        <HealthRow label={t('p99 giữ chỗ', 'p99 reservation')} value="—" />
        <HealthRow label={t('kết nối signalr', 'signalr conns')} value="—" />
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            fontSize: 11,
          }}
        >
          <span style={{ color: 'var(--ink-3)' }}>noisy neighbour</span>
          <Pill kind="ok">{t('ổn định', 'stable')}</Pill>
        </div>
      </div>
    </div>
  );
}

function HealthRow({ label, value }: { label: string; value: string }) {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        fontSize: 11,
      }}
    >
      <span style={{ color: 'var(--ink-3)' }}>{label}</span>
      <span className="mono tnum">{value}</span>
    </div>
  );
}
