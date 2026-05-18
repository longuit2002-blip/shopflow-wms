/**
 * Sidebar — left-rail navigation + System Health placeholder.
 *
 * Ports the design canon `app.jsx` `<Sidebar>` (~line 46). Sprint-6
 * decisions vs canon:
 *
 * - Role is hardcoded to Owner (Sprint-6's vertical slice), so the
 *   admin section + Operator-hides are simplified out. The canon's
 *   Settings / Audit log / Tenants / Onboarding items stay visible
 *   because Owner sees everything; they're just stubbed via ComingSoon
 *   routes (handled by U6).
 *
 * - The "active" prop is supplied by the caller. Sprint-6 hardcodes
 *   `inventory` until U6 wires TanStack Router; once routing lands the
 *   App component derives `active` from `useLocation()`.
 *
 * - System Health values are mock-static. Real values stream from the
 *   `/system/health` SignalR channel in Sprint-7.
 */

import {
  LayoutDashboard,
  Boxes,
  Truck,
  Receipt,
  Plug,
  RefreshCw,
  Settings,
  FileSearch,
  Building2,
  UserPlus,
  type LucideIcon,
} from 'lucide-react';
import { Logo } from '../primitives/Logo';
import { Pill } from '../primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

export type ScreenId =
  | 'dashboard'
  | 'inventory'
  | 'inbound'
  | 'outbound'
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
  count: number | null;
  /** "Coming Sprint-X" badge for stubbed routes. Inventory is null. */
  upcoming?: string;
  /** Section header above the next group. */
  groupBefore?: string;
}

export interface SidebarProps {
  /** Currently active screen id. Sprint-6 callers pass `inventory`. */
  active: ScreenId;
  onNavigate?: (id: ScreenId) => void;
}

export function Sidebar({ active, onNavigate }: SidebarProps) {
  // Re-subscribe to locale so labels re-render when LocaleSwitcher flips lang.
  useLocale();

  const items: NavItem[] = [
    { id: 'dashboard', label: t('Tổng quan', 'Dashboard'), icon: LayoutDashboard, count: null, upcoming: 'Sprint 7' },
    { id: 'inventory', label: t('Tồn kho', 'Inventory'), icon: Boxes, count: null },
    { id: 'inbound', label: t('Nhập hàng', 'Inbound'), icon: Truck, count: null, upcoming: 'Sprint 8' },
    { id: 'outbound', label: t('Đơn hàng', 'Outbound'), icon: Receipt, count: null, upcoming: 'Sprint 7' },
    { id: 'channels', label: t('Kênh bán', 'Channels'), icon: Plug, count: null, upcoming: 'Sprint 8' },
    { id: 'sync', label: t('Đồng bộ tồn', 'Stock sync'), icon: RefreshCw, count: null, upcoming: 'Sprint 8' },
    {
      id: 'settings',
      label: t('Cài đặt', 'Settings'),
      icon: Settings,
      count: null,
      upcoming: 'Phase 3',
      groupBefore: t('Quản trị', 'Admin'),
    },
    { id: 'audit', label: t('Audit log', 'Audit log'), icon: FileSearch, count: null, upcoming: 'Phase 3' },
    { id: 'tenants', label: t('Tenants', 'Tenants'), icon: Building2, count: null, upcoming: 'Phase 3' },
    { id: 'onboarding', label: t('Khởi tạo mới', 'Onboard new'), icon: UserPlus, count: null, upcoming: 'Phase 3' },
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
          <div style={{ fontSize: 13.5, fontWeight: 700, letterSpacing: '-0.01em' }}>
            ShopFlow
          </div>
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
              active={active === it.id}
              onClick={() => onNavigate?.(it.id)}
            />
          ))}
        </nav>

        <SystemHealth />
      </div>
    </aside>
  );
}

interface NavRowProps {
  item: NavItem;
  active: boolean;
  onClick: () => void;
}

function NavRow({ item, active, onClick }: NavRowProps) {
  const { icon: Icon, label, upcoming, groupBefore } = item;
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
      <button
        type="button"
        className={`nav-item ${active ? 'active' : ''}`}
        onClick={onClick}
        aria-current={active ? 'page' : undefined}
        style={{
          width: '100%',
          textAlign: 'left',
          border: 'none',
          background: 'transparent',
          font: 'inherit',
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
      </button>
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
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: 11 }}>
          <span style={{ color: 'var(--ink-3)' }}>noisy neighbour</span>
          <Pill kind="ok">{t('ổn định', 'stable')}</Pill>
        </div>
      </div>
    </div>
  );
}

function HealthRow({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: 11 }}>
      <span style={{ color: 'var(--ink-3)' }}>{label}</span>
      <span className="mono tnum">{value}</span>
    </div>
  );
}
