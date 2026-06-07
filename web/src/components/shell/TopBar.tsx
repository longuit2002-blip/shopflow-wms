/**
 * TopBar — 50 px-tall header above the content area.
 *
 * Ports the design canon `app.jsx` `<TopBar>` (~line 119) at Sprint-6 scope:
 *
 * - TenantPill on the left (single tenant in Sprint-6; multi-tenant
 *   switch dropdown lands with real auth in Sprint-7).
 * - LiveIndicator — placeholder pulsing dot, status hardcoded to "info"
 *   (always "connected"); SignalR ties it to real state in Sprint-7.
 * - LocaleSwitcher — functional VI ↔ EN flip with persistence.
 * - Help button — visual stub. Help overlay shipped in Sprint-7.
 * - Notification bell — visual stub with unread dot. Real notifications
 *   stream from SignalR in Sprint-7.
 * - User chip — Owner name + role label. The role label is hardcoded
 *   to "Chủ tài khoản / Owner" since Sprint-6 only ships that role.
 *
 * Intentionally omitted vs canon: role switcher (single-role sprint),
 * tenant search dropdown, notification dropdown, search/⌘K palette,
 * breach banner (Sprint-7 with SignalR).
 */

import { Bell, HelpCircle } from 'lucide-react';
import { TenantPill } from './TenantPill';
import { LiveIndicator } from './LiveIndicator';
import { LocaleSwitcher } from './LocaleSwitcher';
import { t, useLocale } from '../../hooks/useLocale';

export interface UserProfile {
  /** Display name (e.g. "Nguyễn Văn A"). */
  name: string;
  /** 1–3 char initials for the avatar circle. */
  initials: string;
}

export interface TenantProfile {
  monogram: string;
  legalName: string;
  erc: string;
  region: string;
  dbName: string;
}

export interface TopBarProps {
  tenant: TenantProfile;
  user: UserProfile;
}

export function TopBar({ tenant, user }: TopBarProps) {
  // Re-subscribe so the Owner role label re-renders on locale flip.
  useLocale();

  return (
    <header
      role="banner"
      style={{
        height: 50,
        display: 'flex',
        alignItems: 'center',
        padding: '0 16px',
        borderBottom: '1px solid var(--line)',
        background: 'var(--panel)',
        gap: 10,
        flex: 'none',
        position: 'relative',
        minWidth: 0,
      }}
    >
      <TenantPill {...tenant} />

      <span style={{ flex: 1, minWidth: 8 }} />

      <LiveIndicator />

      <button
        type="button"
        className="btn ghost sm"
        title={t('Trợ giúp · ?', 'Help · ?')}
        aria-label={t('Trợ giúp', 'Help')}
      >
        <HelpCircle size={13} aria-hidden />
      </button>

      <LocaleSwitcher />

      <button
        type="button"
        className="btn ghost sm"
        style={{ position: 'relative' }}
        aria-label={t('Thông báo', 'Notifications')}
      >
        <Bell size={13} aria-hidden />
        <span
          aria-hidden
          style={{
            position: 'absolute',
            top: 4,
            right: 4,
            width: 6,
            height: 6,
            borderRadius: 3,
            background: 'var(--bad)',
          }}
        />
      </button>

      <div style={{ width: 1, height: 28, background: 'var(--line)' }} />

      <UserChip user={user} />
    </header>
  );
}

function UserChip({ user }: { user: UserProfile }) {
  const roleLabel = t('Chủ tài khoản', 'Owner');
  return (
    <div
      className="fs0"
      style={{ display: 'flex', alignItems: 'center', gap: 8, maxWidth: 200 }}
      title={`${user.name} · ${roleLabel}`}
    >
      <div
        className="fs0"
        aria-hidden
        style={{
          width: 28,
          height: 28,
          borderRadius: 14,
          background: 'var(--accent-soft)',
          color: 'var(--accent-ink)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 11,
          fontWeight: 700,
          border: '1px solid var(--accent-line)',
        }}
      >
        {user.initials}
      </div>
      <div style={{ lineHeight: 1.2, minWidth: 0 }}>
        <div className="tr" style={{ fontSize: 12, fontWeight: 600 }}>
          {user.name}
        </div>
        <div className="tr" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
          {roleLabel}
        </div>
      </div>
    </div>
  );
}
