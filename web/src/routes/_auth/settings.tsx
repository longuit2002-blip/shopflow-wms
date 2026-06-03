import { Fragment, useState } from 'react';
import { createFileRoute, Link } from '@tanstack/react-router';
import {
  Settings,
  ChevronRight,
  Building2,
  Users,
  ShieldCheck,
  FileSearch,
  Lock,
  Receipt,
  Bell,
  Plug,
  Info,
  Pencil,
  ArrowDownToLine,
  Archive,
  Trash2,
  Search,
  Download,
  UserPlus,
  X,
  Plus,
  Send,
  ShieldOff,
  MoreHorizontal,
  Check,
  Eye,
  Copy,
  Pipette,
  ImagePlus,
  ExternalLink,
} from 'lucide-react';
import { Pill, type PillKind } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Settings — the largest design-handoff screen (the configuration shell).
 *
 * Ported from the design handoff `screen-settings.jsx`. A two-column layout:
 * a left rail (tiered sub-nav) + a content pane that swaps between the
 * settings sub-screens via local tab state. The tiers, mirroring the
 * design:
 *   - Tier 1 (marquee, full): Compliance + Audit log — these ship as their
 *     own first-class routes (`/compliance`, `/audit`), so the rail
 *     deep-links to them rather than re-embedding the screens.
 *   - Tier 2 (full depth, ported here): Workspace, Members, Roles &
 *     permissions.
 *   - Tier 3 (placeholder): Privacy, Billing, Notifications, Channels & API
 *     — rendered as roadmap stubs.
 *
 * No role switcher exists in the source, so the prototype's operator-lockout
 * branch is dropped; the primary (Owner/manager) view renders directly.
 *
 * Data is mocked in the frontend (no settings backend endpoints exist yet).
 * `data-review` / `data-tour` anchors preserved from the handoff.
 */

// ── Mock tenant + data ─────────────────────────────────────────────────────

const TENANT = {
  legal: 'Yến Sào Khánh Hoà Co., Ltd.',
  erc: 'ERC 4201234567',
};

type SettingsTab =
  | 'workspace'
  | 'members'
  | 'roles'
  | 'privacy'
  | 'billing'
  | 'notifications'
  | 'channels-api';

type RailRoute = '/compliance' | '/audit';

interface RailItem {
  id: SettingsTab;
  icon: typeof Building2;
  vi: string;
  en: string;
  tier: 1 | 2 | 3;
}

interface RailExternalItem {
  id: string;
  to: RailRoute;
  icon: typeof Building2;
  vi: string;
  en: string;
  tier: 1;
}

interface RailSection {
  vi: string;
  en: string;
  items: RailItem[];
  external?: RailExternalItem[];
}

const RAIL_SECTIONS: RailSection[] = [
  {
    vi: 'Tổ chức',
    en: 'Organization',
    items: [
      { id: 'workspace', icon: Building2, vi: 'Workspace', en: 'Workspace', tier: 2 },
      { id: 'members', icon: Users, vi: 'Thành viên', en: 'Members', tier: 2 },
      { id: 'roles', icon: ShieldCheck, vi: 'Vai trò & quyền', en: 'Roles & permissions', tier: 2 },
    ],
  },
  {
    vi: 'Tuân thủ & bảo mật',
    en: 'Compliance & security',
    items: [{ id: 'privacy', icon: Lock, vi: 'Quyền riêng tư', en: 'Privacy', tier: 3 }],
    external: [
      {
        id: 'compliance',
        to: '/compliance',
        icon: ShieldCheck,
        vi: 'Compliance',
        en: 'Compliance',
        tier: 1,
      },
      { id: 'audit', to: '/audit', icon: FileSearch, vi: 'Audit log', en: 'Audit log', tier: 1 },
    ],
  },
  {
    vi: 'Vận hành',
    en: 'Operations',
    items: [
      { id: 'billing', icon: Receipt, vi: 'Thanh toán', en: 'Billing & invoicing', tier: 3 },
      { id: 'notifications', icon: Bell, vi: 'Thông báo', en: 'Notifications', tier: 3 },
      { id: 'channels-api', icon: Plug, vi: 'Kênh & API', en: 'Channels & API', tier: 3 },
    ],
  },
];

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/settings')({
  component: SettingsRouteComponent,
});

function SettingsRouteComponent() {
  useLocale();
  const [tab, setTab] = useState<SettingsTab>('workspace');

  return (
    <div style={{ flex: 1, display: 'flex', minHeight: 0 }} data-tour="settings">
      <SettingsRail tab={tab} setTab={setTab} />
      <div
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          minHeight: 0,
          minWidth: 0,
        }}
      >
        {tab === 'workspace' && <WorkspaceSettings />}
        {tab === 'members' && <MembersSettings />}
        {tab === 'roles' && <RolesSettings />}
        {tab === 'privacy' && (
          <SettingsStub
            icon={Lock}
            title={t('Quyền riêng tư', 'Privacy')}
            blurb={t(
              'Cookie banner, consent log, DSAR queue cho khách hàng cuối.',
              'Cookie banner, consent log, DSAR queue for end-customers.',
            )}
            milestones={[
              'Phase 3 · Q4 2026',
              t('Phụ thuộc legal review', 'Pending legal review'),
              t('Tích hợp với Compliance', 'Integrates with Compliance'),
            ]}
          />
        )}
        {tab === 'billing' && (
          <SettingsStub
            icon={Receipt}
            title={t('Thanh toán', 'Billing & invoicing')}
            blurb={t(
              'Hóa đơn VAT tự động, lịch sử thanh toán, gói cước, tín dụng.',
              'Automated VAT invoicing, payment history, plan, credits.',
            )}
            milestones={[
              'Phase 3 · Q3 2026',
              t('Phụ thuộc dịch vụ Stripe-VN', 'Depends on Stripe-VN'),
              t('Tích hợp Misa cho hóa đơn điện tử', 'Misa e-invoice integration'),
            ]}
          />
        )}
        {tab === 'notifications' && (
          <SettingsStub
            icon={Bell}
            title={t('Thông báo', 'Notifications')}
            blurb={t(
              'Tuỳ chọn email, SMS, in-app cho từng sự kiện: SLA, low-stock, allocation drift.',
              'Email, SMS, in-app preferences for SLA, low-stock, allocation drift.',
            )}
            milestones={[
              'Phase 2 · Q2 2026',
              t('Phụ thuộc SendGrid templates', 'Depends on SendGrid templates'),
              t('Trình sửa quy tắc theo vai trò', 'Per-role rule editor'),
            ]}
          />
        )}
        {tab === 'channels-api' && (
          <SettingsStub
            icon={Plug}
            title={t('Kênh & API', 'Channels & API')}
            blurb={t(
              'Trang OAuth/credentials cho marketplaces, API key management, webhook signing secrets.',
              'OAuth/credentials for marketplaces, API key management, webhook signing secrets.',
            )}
            milestones={[
              'Phase 1.5 · Q2 2026',
              t('Hợp nhất với màn Kênh bán', 'Merges with Channels screen'),
              t('API key tự xoay 90 ngày', 'API keys auto-rotate every 90 days'),
            ]}
          />
        )}
      </div>
    </div>
  );
}

// ── Rail ─────────────────────────────────────────────────────────────────────

function SettingsRail({ tab, setTab }: { tab: SettingsTab; setTab: (t: SettingsTab) => void }) {
  return (
    <aside
      style={{
        width: 220,
        background: 'var(--bg-soft)',
        borderRight: '1px solid var(--line)',
        flex: 'none',
        display: 'flex',
        flexDirection: 'column',
        minHeight: 0,
      }}
    >
      <div style={{ padding: '14px 16px 8px', borderBottom: '1px solid var(--line)' }}>
        <div style={{ fontSize: 13, fontWeight: 600, letterSpacing: '-0.005em' }}>
          {t('Cài đặt', 'Settings')}
        </div>
        <div style={{ fontSize: 10.5, color: 'var(--ink-3)', marginTop: 1 }}>
          {t('cấu hình & tuân thủ tenant', 'tenant config & compliance')}
        </div>
      </div>
      <div className="scroll-y" style={{ flex: 1, padding: '6px 8px 16px' }}>
        {RAIL_SECTIONS.map((sec, si) => (
          <div key={sec.en} style={{ marginTop: si === 0 ? 4 : 10 }}>
            <div
              style={{
                padding: '6px 8px 4px',
                fontSize: 9.5,
                color: 'var(--ink-4)',
                letterSpacing: '0.08em',
                textTransform: 'uppercase',
                fontWeight: 600,
              }}
            >
              {t(sec.vi, sec.en)}
            </div>
            {(sec.external ?? []).map((it) => (
              <Link key={it.id} to={it.to} className="nav-item" style={{ textDecoration: 'none' }}>
                <it.icon size={13} strokeWidth={1.5} aria-hidden />
                <span style={{ flex: 1 }}>{t(it.vi, it.en)}</span>
                <ExternalLink
                  size={10}
                  strokeWidth={1.5}
                  style={{ color: 'var(--ink-4)' }}
                  aria-hidden
                />
                <TierBadge tier={it.tier} />
              </Link>
            ))}
            {sec.items.map((it) => {
              const active = tab === it.id;
              return (
                <div
                  key={it.id}
                  onClick={() => setTab(it.id)}
                  className={'nav-item' + (active ? ' active' : '')}
                >
                  <it.icon size={13} strokeWidth={1.5} aria-hidden />
                  <span style={{ flex: 1 }}>{t(it.vi, it.en)}</span>
                  <TierBadge tier={it.tier} />
                </div>
              );
            })}
          </div>
        ))}
      </div>
      <div
        style={{
          padding: '10px 14px',
          borderTop: '1px solid var(--line)',
          fontSize: 10.5,
          color: 'var(--ink-3)',
          lineHeight: 1.5,
        }}
      >
        <div className="lbl" style={{ marginBottom: 4 }}>
          {t('Chú giải Tier', 'Tier legend')}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 2 }}>
          <span className="tier-badge tier-1">1</span> {t('Đầy đủ — marquee', 'Full — marquee')}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 2 }}>
          <span className="tier-badge tier-2">2</span> {t('Đầy đủ', 'Full depth')}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 2 }}>
          <span className="tier-badge tier-3">3</span> {t('Placeholder', 'Placeholder')}
        </div>
      </div>
    </aside>
  );
}

function TierBadge({ tier }: { tier: 1 | 2 | 3 }) {
  const title =
    tier === 1
      ? t('Tier 1 · trọng tâm', 'Tier 1 · marquee')
      : tier === 2
        ? t('Tier 2 · đầy đủ', 'Tier 2 · full')
        : t('Tier 3 · placeholder', 'Tier 3 · placeholder');
  return (
    <span className={`tier-badge tier-${tier}`} title={title}>
      {tier}
    </span>
  );
}

function SettingsBreadcrumb({ crumb, sub }: { crumb: string; sub?: string }) {
  return (
    <div
      style={{
        padding: '12px 24px 0',
        display: 'flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 11.5,
        color: 'var(--ink-3)',
      }}
    >
      <Settings size={11} strokeWidth={1.5} aria-hidden />
      <span>{t('Cài đặt', 'Settings')}</span>
      <ChevronRight size={11} strokeWidth={1.5} style={{ color: 'var(--ink-4)' }} aria-hidden />
      <span style={{ color: 'var(--ink)', fontWeight: 500 }}>{crumb}</span>
      {sub && (
        <Fragment>
          <span style={{ color: 'var(--ink-4)' }}>·</span>
          <span>{sub}</span>
        </Fragment>
      )}
    </div>
  );
}

// ── Workspace ────────────────────────────────────────────────────────────────

function WorkspaceSettings() {
  return (
    <div className="scroll-y" style={{ flex: 1 }}>
      <SettingsBreadcrumb
        crumb={t('Workspace', 'Workspace')}
        sub={t(
          'Pháp nhân · vùng làm việc · giờ vận hành · branding',
          'Legal entity · region · business hours · branding',
        )}
      />
      <div
        data-review="workspace"
        style={{
          padding: '14px 24px 40px',
          display: 'grid',
          gridTemplateColumns: '1fr',
          gap: 18,
          maxWidth: 1100,
        }}
      >
        <Panel
          title={t('Pháp nhân & định danh', 'Legal entity & identity')}
          desc={t(
            'Thông tin xuất hiện trên hoá đơn, hợp đồng, và audit log.',
            'Used on invoices, contracts, and audit log.',
          )}
        >
          <Grid2>
            <Field
              label={t('Tên pháp lý', 'Legal name')}
              value={TENANT.legal}
              hint={t(
                'Phải khớp giấy chứng nhận đăng ký doanh nghiệp.',
                'Must match the business registration certificate.',
              )}
            />
            <Field label="Tax / ERC" value={TENANT.erc} mono />
            <Field label={t('Tên hiển thị', 'Display name')} value="Yến Sào Khánh Hoà" />
            <Field label={t('Múi giờ', 'Timezone')} value="Asia/Ho_Chi_Minh (UTC+7)" />
            <Field label={t('Tiền tệ', 'Currency')} value="VND · ₫" />
            <Field
              label={t('Ngôn ngữ', 'Locale')}
              value={t('Tiếng Việt (vi-VN) · mặc định', 'Vietnamese (vi-VN) · default')}
            />
          </Grid2>
        </Panel>

        <Panel
          title={t('Vùng làm việc', 'Operating region')}
          desc={t(
            'Quyết định nơi lưu trữ dữ liệu (data residency) — không thể đổi sau khi kích hoạt.',
            'Determines data residency — locked after activation.',
          )}
        >
          <div style={{ display: 'flex', gap: 12 }}>
            <RegionCard
              flag="🇻🇳"
              name="Việt Nam"
              sub={t('Khu vực chính · sg-1', 'Primary region · sg-1')}
              active
            />
            <RegionCard
              flag="🌏"
              name={t('Đông Nam Á', 'Southeast Asia')}
              sub={t('Chưa khả dụng — Phase 4', 'Unavailable — Phase 4')}
            />
            <RegionCard
              flag="🌐"
              name="Multi-region"
              sub={t('Mid-Market & Enterprise', 'Mid-Market & Enterprise')}
            />
          </div>
          <div
            style={{
              marginTop: 12,
              padding: 10,
              background: 'var(--info-soft)',
              border: '1px solid var(--info-line)',
              borderRadius: 3,
              fontSize: 11.5,
              color: 'var(--info-ink, var(--ink-2))',
              display: 'flex',
              gap: 8,
              alignItems: 'flex-start',
            }}
          >
            <Info size={13} strokeWidth={1.75} style={{ marginTop: 1, flex: 'none' }} aria-hidden />
            <span>
              {t(
                'Mọi snapshot, replica, và sub-processor đều bị giới hạn trong khu vực này. Xem chi tiết trong Compliance.',
                'All snapshots, replicas, and sub-processors are confined to this region. See Compliance for detail.',
              )}
            </span>
          </div>
        </Panel>

        <Panel
          title={t('Giờ vận hành', 'Business hours')}
          desc={t(
            'Ảnh hưởng SLA cảnh báo, lịch chạy webhook, và báo cáo cuối ngày.',
            'Drives SLA alerts, webhook scheduling, end-of-day reports.',
          )}
        >
          <BusinessHoursEditor />
        </Panel>

        <Panel
          title={t('Thương hiệu', 'Branding')}
          desc={t(
            'Logo và màu hiển thị trong email cảnh báo, trang đăng nhập, và PDF xuất.',
            'Logo and color used in alert emails, login page, exported PDFs.',
          )}
        >
          <BrandingEditor />
        </Panel>

        <Panel
          title={t('Vùng nguy hiểm', 'Danger zone')}
          danger
          desc={t(
            'Hành động không thể phục hồi. Tất cả yêu cầu Owner + MFA + làm nguội 7 ngày.',
            'Irreversible actions. All require Owner + MFA + 7-day cool-off.',
          )}
        >
          <DangerRow
            icon={ArrowDownToLine}
            title={t('Tạm dừng tenant', 'Suspend tenant')}
            desc={t(
              'Đăng xuất tất cả thành viên, đặt CSDL ở chế độ chỉ đọc. Có thể bật lại.',
              'Sign out all members, set DB read-only. Reversible.',
            )}
            cta={t('Tạm dừng', 'Suspend')}
          />
          <DangerRow
            icon={Archive}
            title={t('Bắt đầu lưu trữ', 'Begin archive')}
            desc={t(
              'Chuyển tenant về trạng thái Archive-pending. DROP DATABASE sau 365 ngày.',
              'Move tenant to Archive-pending state. DROP DATABASE after 365 days.',
            )}
            cta={t('Bắt đầu', 'Begin')}
          />
          <DangerRow
            icon={Trash2}
            title={t('Xoá tenant vĩnh viễn', 'Permanently delete tenant')}
            desc={t(
              'Quy trình 3 bước có cảnh báo. Xem chi tiết tại Compliance.',
              '3-step destructive flow. Detail in Compliance.',
            )}
            cta={t('Xoá tenant', 'Delete tenant')}
            critical
          />
        </Panel>
      </div>
    </div>
  );
}

function Panel({
  title,
  desc,
  children,
  danger,
}: {
  title: string;
  desc?: string;
  children: React.ReactNode;
  danger?: boolean;
}) {
  return (
    <section
      style={{
        border: '1px solid ' + (danger ? 'var(--bad-line)' : 'var(--line)'),
        borderRadius: 'var(--radius-lg)',
        background: 'var(--panel)',
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          padding: '14px 18px 12px',
          borderBottom: '1px solid var(--line)',
          background: danger ? 'var(--bad-soft)' : 'var(--bg-soft)',
        }}
      >
        <div
          style={{ fontSize: 14, fontWeight: 600, color: danger ? 'var(--bad-ink)' : 'var(--ink)' }}
        >
          {title}
        </div>
        {desc && <div style={{ fontSize: 11.5, color: 'var(--ink-3)', marginTop: 2 }}>{desc}</div>}
      </div>
      <div style={{ padding: 18 }}>{children}</div>
    </section>
  );
}

function Grid2({ children }: { children: React.ReactNode }) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
        gap: 12,
      }}
    >
      {children}
    </div>
  );
}

function Field({
  label,
  value,
  mono,
  hint,
}: {
  label: string;
  value: string;
  mono?: boolean;
  hint?: string;
}) {
  return (
    <div>
      <div className="lbl" style={{ marginBottom: 3 }}>
        {label}
      </div>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 6,
          padding: '7px 10px',
          background: 'var(--bg-soft)',
          border: '1px solid var(--line)',
          borderRadius: 3,
          minHeight: 32,
        }}
      >
        <span
          className={mono ? 'mono' : ''}
          style={{ fontSize: 12.5, flex: 1, fontWeight: mono ? 500 : 400 }}
        >
          {value}
        </span>
        <Pencil size={11} strokeWidth={1.5} style={{ color: 'var(--ink-4)' }} aria-hidden />
      </div>
      {hint && <div style={{ fontSize: 10.5, color: 'var(--ink-3)', marginTop: 4 }}>{hint}</div>}
    </div>
  );
}

function RegionCard({
  flag,
  name,
  sub,
  active,
}: {
  flag: string;
  name: string;
  sub: string;
  active?: boolean;
}) {
  return (
    <div
      style={{
        flex: 1,
        padding: '12px 14px',
        border: '1px solid ' + (active ? 'var(--accent)' : 'var(--line)'),
        borderTop: active ? '3px solid var(--accent)' : '1px solid var(--line)',
        borderRadius: 3,
        background: active ? 'var(--accent-soft)' : 'var(--bg-soft)',
        opacity: active ? 1 : 0.7,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
        <span style={{ fontSize: 18 }}>{flag}</span>
        <span style={{ fontSize: 13, fontWeight: 600 }}>{name}</span>
        {active && <Pill kind="ok">{t('hiện tại', 'current')}</Pill>}
      </div>
      <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>{sub}</div>
    </div>
  );
}

interface DayLabel {
  vi: string;
  en: string;
}

interface DayHours {
  open: string | null;
  close: string | null;
}

const DAYS: DayLabel[] = [
  { vi: 'Thứ 2', en: 'Mon' },
  { vi: 'Thứ 3', en: 'Tue' },
  { vi: 'Thứ 4', en: 'Wed' },
  { vi: 'Thứ 5', en: 'Thu' },
  { vi: 'Thứ 6', en: 'Fri' },
  { vi: 'Thứ 7', en: 'Sat' },
  { vi: 'CN', en: 'Sun' },
];

const HOURS: DayHours[] = [
  { open: '08:00', close: '17:30' },
  { open: '08:00', close: '17:30' },
  { open: '08:00', close: '17:30' },
  { open: '08:00', close: '17:30' },
  { open: '08:00', close: '17:30' },
  { open: '08:00', close: '12:00' },
  { open: null, close: null },
];

function BusinessHoursEditor() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {DAYS.map((d, i) => {
        const h = HOURS[i]!;
        const closed = h.open === null;
        return (
          <div
            key={d.en}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 12,
              padding: '8px 10px',
              background: 'var(--bg-soft)',
              border: '1px solid var(--line)',
              borderRadius: 3,
            }}
          >
            <div style={{ width: 64, fontSize: 12, fontWeight: 500 }}>{t(d.vi, d.en)}</div>
            <label
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 6,
                fontSize: 11.5,
                cursor: 'pointer',
              }}
            >
              <input type="checkbox" defaultChecked={!closed} />
              <span style={{ color: closed ? 'var(--ink-3)' : 'var(--ink)' }}>
                {closed ? t('Nghỉ', 'Closed') : t('Mở', 'Open')}
              </span>
            </label>
            {!closed && (
              <Fragment>
                <span
                  className="mono"
                  style={{
                    fontSize: 12,
                    padding: '4px 8px',
                    background: 'var(--panel)',
                    border: '1px solid var(--line)',
                    borderRadius: 2,
                  }}
                >
                  {h.open}
                </span>
                <span style={{ color: 'var(--ink-4)' }}>→</span>
                <span
                  className="mono"
                  style={{
                    fontSize: 12,
                    padding: '4px 8px',
                    background: 'var(--panel)',
                    border: '1px solid var(--line)',
                    borderRadius: 2,
                  }}
                >
                  {h.close}
                </span>
              </Fragment>
            )}
            <span style={{ flex: 1 }} />
            {i === 5 && <Pill kind="warn">{t('rút ngắn 12/05', 'shortened 12/05')}</Pill>}
          </div>
        );
      })}
      <div
        style={{
          display: 'flex',
          gap: 8,
          alignItems: 'center',
          marginTop: 4,
          fontSize: 11.5,
          color: 'var(--ink-3)',
        }}
      >
        <Info size={12} strokeWidth={1.5} aria-hidden />
        <span>
          {t(
            'SLA cảnh báo và lịch webhook đều theo giờ vận hành ở đây.',
            'SLA alerts and webhook scheduling follow these hours.',
          )}
        </span>
      </div>
    </div>
  );
}

const BRAND_COLORS = ['#8b6a2b', '#1d4d3a', '#9e3f3a', '#3a5278', '#5a4878', '#2b2b2b'];

function BrandingEditor() {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: '220px 1fr',
        gap: 18,
        alignItems: 'flex-start',
      }}
    >
      <div>
        <div className="lbl" style={{ marginBottom: 6 }}>
          {t('Logo', 'Logo')}
        </div>
        <div
          style={{
            aspectRatio: '4 / 3',
            background: 'var(--bg-sunken)',
            border: '1px dashed var(--line-strong)',
            borderRadius: 3,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexDirection: 'column',
            gap: 6,
            color: 'var(--ink-3)',
          }}
        >
          <ImagePlus size={20} strokeWidth={1.5} aria-hidden />
          <div style={{ fontSize: 11 }}>{t('Kéo thả PNG / SVG', 'Drop PNG / SVG')}</div>
          <div style={{ fontSize: 10, color: 'var(--ink-4)' }}>
            {t('tối đa 2 MB · nền trong suốt', 'max 2 MB · transparent')}
          </div>
        </div>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div>
          <div className="lbl" style={{ marginBottom: 6 }}>
            {t('Màu thương hiệu chính', 'Primary brand color')}
          </div>
          <div style={{ display: 'flex', gap: 6 }}>
            {BRAND_COLORS.map((c, i) => (
              <button
                key={c}
                className="nb"
                type="button"
                title={c}
                aria-label={c}
                style={{
                  width: 32,
                  height: 32,
                  borderRadius: 4,
                  background: c,
                  border: i === 0 ? '2px solid var(--ink)' : '1px solid var(--line)',
                  cursor: 'pointer',
                }}
              />
            ))}
            <button className="btn sm" type="button" style={{ height: 32 }}>
              <Pipette size={12} strokeWidth={1.5} aria-hidden /> Custom
            </button>
          </div>
        </div>
        <div>
          <div className="lbl" style={{ marginBottom: 6 }}>
            {t('Footer hoá đơn', 'Invoice footer')}
          </div>
          <textarea
            aria-label={t('Footer hoá đơn', 'Invoice footer')}
            defaultValue={t(
              'Cảm ơn quý khách đã tin dùng Yến Sào Khánh Hoà. Mọi thắc mắc: hotline 1800-2566.',
              'Thank you for choosing Yến Sào Khánh Hoà. Contact: 1800-2566.',
            )}
            style={{
              width: '100%',
              minHeight: 60,
              padding: '8px 10px',
              background: 'var(--bg-soft)',
              border: '1px solid var(--line)',
              borderRadius: 3,
              fontSize: 12,
              resize: 'vertical',
            }}
          />
        </div>
      </div>
    </div>
  );
}

function DangerRow({
  icon: IconCmp,
  title,
  desc,
  cta,
  critical,
}: {
  icon: typeof Trash2;
  title: string;
  desc: string;
  cta: string;
  critical?: boolean;
}) {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 14,
        padding: '12px 4px',
        borderTop: '1px solid var(--line)',
      }}
    >
      <div
        className="fs0"
        style={{
          width: 32,
          height: 32,
          borderRadius: 3,
          background: 'var(--bad-soft)',
          color: 'var(--bad-ink)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          border: '1px solid var(--bad-line)',
        }}
      >
        <IconCmp size={14} strokeWidth={1.5} aria-hidden />
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 13, fontWeight: 600 }}>{title}</div>
        <div style={{ fontSize: 11.5, color: 'var(--ink-2)', marginTop: 1, lineHeight: 1.4 }}>
          {desc}
        </div>
      </div>
      <button
        className="btn"
        type="button"
        style={{
          borderColor: critical ? 'var(--bad)' : 'var(--bad-line)',
          color: critical ? 'var(--ink-inv)' : 'var(--bad-ink)',
          background: critical ? 'var(--bad)' : 'transparent',
          flex: 'none',
        }}
      >
        {cta}
      </button>
    </div>
  );
}

// ── Members ────────────────────────────────────────────────────────────────

type MemberStatus = 'active' | 'invited' | 'suspended';

interface Member {
  name: string;
  email: string;
  role: string;
  status: MemberStatus;
  mfa: boolean | null;
  last: string;
  joined: string;
  init: string;
  inviteExpires?: string;
}

const MEMBERS: Member[] = [
  {
    name: 'Trần Minh Khôi',
    email: 'khoi@yensaokh.vn',
    role: 'Owner',
    status: 'active',
    mfa: true,
    last: '14:23 hôm nay',
    joined: '2024-08-12',
    init: 'TK',
  },
  {
    name: 'Lê Thị Hồng Vân',
    email: 'van.le@yensaokh.vn',
    role: 'Ops Manager',
    status: 'active',
    mfa: true,
    last: '13:18 hôm nay',
    joined: '2024-09-04',
    init: 'VL',
  },
  {
    name: 'Nguyễn Văn Hùng',
    email: 'hung.nguyen@yensaokh.vn',
    role: 'Seller',
    status: 'active',
    mfa: true,
    last: '11:42 hôm nay',
    joined: '2024-09-11',
    init: 'NH',
  },
  {
    name: 'Phạm Văn Đức',
    email: 'duc.pham@yensaokh.vn',
    role: 'Warehouse Op',
    status: 'active',
    mfa: true,
    last: '14:21 hôm nay',
    joined: '2024-10-23',
    init: 'DP',
  },
  {
    name: 'Nguyễn Hoài Nam',
    email: 'nam.nguyen@yensaokh.vn',
    role: 'Warehouse Op',
    status: 'active',
    mfa: true,
    last: '13:05 hôm nay',
    joined: '2025-01-08',
    init: 'NN',
  },
  {
    name: 'Trần Thị Mai',
    email: 'mai.tran@yensaokh.vn',
    role: 'Warehouse Op',
    status: 'active',
    mfa: false,
    last: 'hôm qua 16:42',
    joined: '2025-02-14',
    init: 'TM',
  },
  {
    name: 'Lý Quốc Việt',
    email: 'viet.ly@yensaokh.vn',
    role: 'Seller',
    status: 'active',
    mfa: true,
    last: '10:18 hôm nay',
    joined: '2025-03-02',
    init: 'LV',
  },
  {
    name: 'Đặng Thu Hương',
    email: 'huong.dang@yensaokh.vn',
    role: 'Read-Only',
    status: 'active',
    mfa: true,
    last: '09:33 hôm nay',
    joined: '2025-04-19',
    init: 'TH',
  },
  {
    name: 'Vũ Hoàng Long',
    email: 'long.vu@yensaokh.vn',
    role: 'Seller',
    status: 'invited',
    mfa: null,
    last: '—',
    joined: '—',
    init: 'VL',
    inviteExpires: '19/05/2026',
  },
  {
    name: 'Trần Phong',
    email: 'phong.tran@yensaokh.vn',
    role: 'Warehouse Op',
    status: 'invited',
    mfa: null,
    last: '—',
    joined: '—',
    init: 'TP',
    inviteExpires: '19/05/2026',
  },
  {
    name: 'Hoàng Mỹ Linh',
    email: 'linh.hoang@yensaokh.vn',
    role: 'Read-Only',
    status: 'suspended',
    mfa: true,
    last: '03/04/2026',
    joined: '2024-12-01',
    init: 'HL',
  },
  {
    name: 'Bùi Anh Tuấn',
    email: 'tuan.bui@yensaokh.vn',
    role: 'Warehouse Op',
    status: 'suspended',
    mfa: false,
    last: '15/03/2026',
    joined: '2024-11-12',
    init: 'BT',
  },
];

type MemberFilter = 'all' | MemberStatus;

function MembersSettings() {
  const [inviteOpen, setInviteOpen] = useState(false);
  const [filter, setFilter] = useState<MemberFilter>('all');

  const counts: Record<MemberFilter, number> = {
    all: MEMBERS.length,
    active: MEMBERS.filter((m) => m.status === 'active').length,
    invited: MEMBERS.filter((m) => m.status === 'invited').length,
    suspended: MEMBERS.filter((m) => m.status === 'suspended').length,
  };
  const filtered = filter === 'all' ? MEMBERS : MEMBERS.filter((m) => m.status === filter);

  const filterTabs: Array<[MemberFilter, string]> = [
    ['all', t('Tất cả', 'All')],
    ['active', t('Hoạt động', 'Active')],
    ['invited', t('Đang mời', 'Invited')],
    ['suspended', t('Đình chỉ', 'Suspended')],
  ];

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <SettingsBreadcrumb
        crumb={t('Thành viên', 'Members')}
        sub={`${MEMBERS.length} ${t('người', 'people')} · 3 ${t('vai trò đang dùng', 'roles in use')}`}
      />

      <div className="strip">
        <span className="t">
          {MEMBERS.length} {t('thành viên', 'members')}
        </span>
        <span style={{ flex: 1 }} />
        <MemberStat
          label={t('Hoạt động', 'Active')}
          value={counts.active}
          sub={t('có MFA', 'MFA-on')}
          kind="ok"
        />
        <span style={{ width: 24 }} />
        <MemberStat
          label={t('Đang mời', 'Invited')}
          value={counts.invited}
          sub={t('hết hạn 7 ngày', 'expires in 7d')}
          kind="info"
        />
        <span style={{ width: 24 }} />
        <MemberStat
          label={t('Đình chỉ', 'Suspended')}
          value={counts.suspended}
          sub={t('cần xem xét', 'review needed')}
          kind="warn"
        />
      </div>

      <div
        className="hairline-b"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          padding: '10px 18px',
          alignItems: 'center',
          background: 'var(--bg-soft)',
        }}
      >
        <div style={{ position: 'relative', flex: '0 0 260px' }}>
          <Search
            size={13}
            style={{
              position: 'absolute',
              left: 8,
              top: '50%',
              transform: 'translateY(-50%)',
              color: 'var(--ink-3)',
            }}
            aria-hidden
          />
          <input
            type="search"
            placeholder={t('Tên hoặc email…', 'Name or email…')}
            style={{ paddingLeft: 26, width: '100%' }}
          />
        </div>
        <div style={{ display: 'flex', gap: 4 }}>
          {filterTabs.map(([k, l]) => (
            <button
              key={k}
              className={'btn sm' + (filter === k ? ' primary' : '')}
              type="button"
              onClick={() => setFilter(k)}
              style={{ height: 28 }}
            >
              {l} <span style={{ marginLeft: 6, opacity: 0.7, fontSize: 10.5 }}>{counts[k]}</span>
            </button>
          ))}
        </div>
        <span style={{ flex: 1 }} />
        <button className="btn sm" type="button">
          <Download size={11} strokeWidth={1.5} aria-hidden /> {t('Xuất CSV', 'Export CSV')}
        </button>
        <button className="btn sm primary" type="button" onClick={() => setInviteOpen(true)}>
          <UserPlus size={11} strokeWidth={1.5} aria-hidden />{' '}
          {t('Mời thành viên', 'Invite member')}
        </button>
      </div>

      <div className="scroll-y" style={{ flex: 1 }}>
        <table className="t-data">
          <thead>
            <tr>
              <th>{t('Tên', 'Name')}</th>
              <th style={{ width: 160 }}>{t('Vai trò', 'Role')}</th>
              <th style={{ width: 110 }}>{t('Trạng thái', 'Status')}</th>
              <th style={{ width: 90 }}>MFA</th>
              <th style={{ width: 170 }}>{t('Lần truy cập gần nhất', 'Last activity')}</th>
              <th style={{ width: 110 }}>{t('Tham gia', 'Joined')}</th>
              <th style={{ width: 40 }} />
            </tr>
          </thead>
          <tbody>
            {filtered.map((m) => {
              const isOwner = m.role === 'Owner';
              return (
                <tr key={m.email} style={{ cursor: 'pointer' }}>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                      <div
                        className="fs0"
                        style={{
                          width: 26,
                          height: 26,
                          borderRadius: 13,
                          background:
                            m.status === 'invited' ? 'var(--bg-sunken)' : 'var(--accent-soft)',
                          color: m.status === 'invited' ? 'var(--ink-3)' : 'var(--accent-ink)',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          fontSize: 10,
                          fontWeight: 700,
                          border:
                            '1px solid ' +
                            (m.status === 'invited' ? 'var(--line)' : 'var(--accent-line)'),
                          opacity: m.status === 'suspended' ? 0.5 : 1,
                        }}
                      >
                        {m.init}
                      </div>
                      <div style={{ minWidth: 0 }}>
                        <div
                          style={{
                            fontSize: 12.5,
                            fontWeight: 500,
                            opacity: m.status === 'suspended' ? 0.6 : 1,
                          }}
                        >
                          {m.name}{' '}
                          {isOwner && (
                            <span
                              style={{
                                fontSize: 9.5,
                                color: 'var(--accent-ink)',
                                marginLeft: 4,
                                padding: '1px 5px',
                                background: 'var(--accent-soft)',
                                borderRadius: 2,
                                fontWeight: 600,
                                letterSpacing: '0.04em',
                                textTransform: 'uppercase',
                              }}
                            >
                              Owner
                            </span>
                          )}
                        </div>
                        <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
                          {m.email}
                        </div>
                      </div>
                    </div>
                  </td>
                  <td>
                    <Pill kind={m.role === 'Owner' ? 'info' : 'default'}>{m.role}</Pill>
                  </td>
                  <td>
                    {m.status === 'active' ? (
                      <Pill kind="ok">{t('hoạt động', 'active')}</Pill>
                    ) : m.status === 'invited' ? (
                      <Pill kind="info">{t('đang mời', 'invited')}</Pill>
                    ) : (
                      <Pill kind="default">{t('đình chỉ', 'suspended')}</Pill>
                    )}
                  </td>
                  <td>
                    {m.mfa === null ? (
                      <span style={{ color: 'var(--ink-4)' }}>—</span>
                    ) : m.mfa ? (
                      <span
                        style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 4,
                          fontSize: 11.5,
                          color: 'var(--ok-ink)',
                        }}
                      >
                        <ShieldCheck size={11} strokeWidth={2} aria-hidden /> on
                      </span>
                    ) : (
                      <span
                        style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 4,
                          fontSize: 11.5,
                          color: 'var(--warn-ink)',
                        }}
                      >
                        <ShieldOff size={11} strokeWidth={2} aria-hidden /> off
                      </span>
                    )}
                  </td>
                  <td style={{ fontSize: 11.5, color: 'var(--ink-2)' }}>
                    {m.status === 'invited' && m.inviteExpires ? (
                      <span style={{ color: 'var(--ink-3)' }}>
                        {t('lời mời hết hạn', 'invite expires')} {m.inviteExpires}
                      </span>
                    ) : (
                      m.last
                    )}
                  </td>
                  <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
                    {m.joined}
                  </td>
                  <td>
                    <MoreHorizontal size={13} style={{ color: 'var(--ink-4)' }} aria-hidden />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {inviteOpen && <InviteDrawer onClose={() => setInviteOpen(false)} />}
    </div>
  );
}

function MemberStat({
  label,
  value,
  sub,
  kind,
}: {
  label: string;
  value: number;
  sub: string;
  kind: PillKind;
}) {
  const color =
    kind === 'ok'
      ? 'var(--ok-ink)'
      : kind === 'warn'
        ? 'var(--warn-ink)'
        : kind === 'info'
          ? 'var(--info-ink, var(--ink-2))'
          : 'var(--ink)';
  return (
    <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      <div className="lbl">{label}</div>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}>
        <span className="mono tnum" style={{ fontSize: 16, fontWeight: 700, color }}>
          {value}
        </span>
        <span style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>{sub}</span>
      </div>
    </div>
  );
}

const INVITE_ROLES = ['Owner', 'Ops Manager', 'Seller', 'Warehouse Op', 'Read-Only'];

interface InviteRow {
  email: string;
  role: string;
}

function InviteDrawer({ onClose }: { onClose: () => void }) {
  const [rows, setRows] = useState<InviteRow[]>([{ email: '', role: 'Warehouse Op' }]);
  const [includeMsg, setIncludeMsg] = useState(false);
  const firstRole = rows[0]?.role ?? 'Warehouse Op';

  return (
    <Fragment>
      <div className="drawer-mask" onClick={onClose} />
      <div
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-label={t('Mời thành viên mới', 'Invite new members')}
        style={{ width: 540 }}
      >
        <div
          style={{
            padding: '14px 18px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
          }}
        >
          <UserPlus size={15} strokeWidth={1.5} aria-hidden />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 13.5, fontWeight: 600 }}>
              {t('Mời thành viên mới', 'Invite new members')}
            </div>
            <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              {t(
                'Lời mời hết hạn sau 7 ngày · không tốn ghế cho đến khi chấp nhận',
                "Invite expires in 7 days · doesn't consume a seat until accepted",
              )}
            </div>
          </div>
          <button
            className="btn ghost sm"
            type="button"
            onClick={onClose}
            aria-label={t('Đóng', 'Close')}
          >
            <X size={14} aria-hidden />
          </button>
        </div>

        <div className="scroll-y" style={{ flex: 1, padding: 18 }}>
          <div className="lbl" style={{ marginBottom: 8 }}>
            {t('Email & vai trò', 'Email & role')}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {rows.map((r, i) => (
              <div key={i} style={{ display: 'flex', gap: 8 }}>
                <input
                  type="email"
                  placeholder="name@yensaokh.vn"
                  value={r.email}
                  onChange={(e) =>
                    setRows(rows.map((x, j) => (j === i ? { ...x, email: e.target.value } : x)))
                  }
                  style={{ flex: 1 }}
                />
                <select
                  value={r.role}
                  onChange={(e) =>
                    setRows(rows.map((x, j) => (j === i ? { ...x, role: e.target.value } : x)))
                  }
                  style={{ width: 160 }}
                  aria-label={t('Vai trò', 'Role')}
                >
                  {INVITE_ROLES.map((ro) => (
                    <option key={ro} value={ro}>
                      {ro}
                    </option>
                  ))}
                </select>
                <button
                  className="btn ghost sm"
                  type="button"
                  onClick={() => setRows(rows.filter((_, j) => j !== i))}
                  disabled={rows.length === 1}
                  aria-label={t('Xoá dòng', 'Remove row')}
                >
                  <X size={11} aria-hidden />
                </button>
              </div>
            ))}
            <button
              className="btn sm"
              type="button"
              style={{ alignSelf: 'flex-start' }}
              onClick={() => setRows([...rows, { email: '', role: 'Warehouse Op' }])}
            >
              <Plus size={11} aria-hidden /> {t('Thêm dòng', 'Add row')}
            </button>
          </div>

          <div style={{ marginTop: 18 }}>
            <label
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                fontSize: 12,
                cursor: 'pointer',
              }}
            >
              <input
                type="checkbox"
                checked={includeMsg}
                onChange={(e) => setIncludeMsg(e.target.checked)}
              />
              {t('Thêm tin nhắn cá nhân vào email mời', 'Add a personal note to the invite email')}
            </label>
            {includeMsg && (
              <textarea
                placeholder={t(
                  'Chào em, bên team kho cần thêm tay nhặt hàng cho ca chiều...',
                  'Hi, the warehouse team needs more pickers for the afternoon shift...',
                )}
                style={{
                  width: '100%',
                  minHeight: 70,
                  marginTop: 8,
                  padding: '8px 10px',
                  background: 'var(--bg-soft)',
                  border: '1px solid var(--line)',
                  borderRadius: 3,
                  fontSize: 12,
                  resize: 'vertical',
                }}
              />
            )}
          </div>

          <div
            style={{
              marginTop: 20,
              padding: 12,
              background: 'var(--bg-soft)',
              border: '1px solid var(--line)',
              borderRadius: 3,
            }}
          >
            <div className="lbl" style={{ marginBottom: 6 }}>
              {t('Xem trước email', 'Email preview')}
            </div>
            <div style={{ fontSize: 11.5, color: 'var(--ink-2)', lineHeight: 1.6 }}>
              {t(
                'Khôi · Yến Sào Khánh Hoà đã mời bạn vào ShopFlow với vai trò',
                'Khôi · Yến Sào Khánh Hoà has invited you to ShopFlow as',
              )}{' '}
              <strong>{firstRole}</strong>.{' '}
              {t(
                'Nhấn vào nút bên dưới để tham gia. Lời mời hết hạn ngày 19/05/2026.',
                'Click below to accept. Invite expires 19/05/2026.',
              )}
            </div>
            <div
              style={{
                marginTop: 10,
                padding: '8px 12px',
                background: 'var(--ink)',
                color: 'var(--ink-inv)',
                textAlign: 'center',
                fontSize: 11.5,
                fontWeight: 600,
                borderRadius: 3,
                maxWidth: 200,
              }}
            >
              {t('Chấp nhận lời mời', 'Accept invitation')}
            </div>
          </div>
        </div>

        <div
          style={{
            padding: 14,
            borderTop: '1px solid var(--line)',
            background: 'var(--bg-soft)',
            display: 'flex',
            gap: 8,
          }}
        >
          <button className="btn" type="button" onClick={onClose}>
            {t('Huỷ', 'Cancel')}
          </button>
          <span style={{ flex: 1 }} />
          <button className="btn primary" type="button" onClick={onClose}>
            <Send size={12} strokeWidth={1.5} aria-hidden />{' '}
            {t(
              `Gửi ${rows.length} lời mời`,
              `Send ${rows.length} invite${rows.length > 1 ? 's' : ''}`,
            )}
          </button>
        </div>
      </div>
    </Fragment>
  );
}

// ── Roles & permissions ──────────────────────────────────────────────────────

type RoleId = 'owner' | 'ops' | 'seller' | 'wh' | 'ro';

interface RoleDef {
  id: RoleId;
  name: string;
  count: number;
  locked: boolean;
  desc_vi: string;
  desc_en: string;
}

const ROLES: RoleDef[] = [
  {
    id: 'owner',
    name: 'Owner',
    count: 1,
    locked: true,
    desc_vi: 'Toàn quyền · không thể bị xoá · ít nhất 1 người',
    desc_en: 'Full access · cannot be removed · always ≥1',
  },
  {
    id: 'ops',
    name: 'Ops Manager',
    count: 1,
    locked: false,
    desc_vi: 'Quản trị tenant · không truy cập billing',
    desc_en: 'Tenant admin · no billing access',
  },
  {
    id: 'seller',
    name: 'Seller',
    count: 3,
    locked: false,
    desc_vi: 'Tạo đơn · quản lý SKU · xem báo cáo',
    desc_en: 'Create orders · manage SKUs · view reports',
  },
  {
    id: 'wh',
    name: 'Warehouse Op',
    count: 5,
    locked: false,
    desc_vi: 'Nhặt · đóng gói · điều chỉnh tồn · không xem giá',
    desc_en: 'Pick · pack · stock adjust · no pricing',
  },
  {
    id: 'ro',
    name: 'Read-Only',
    count: 2,
    locked: false,
    desc_vi: 'Xem báo cáo · xuất CSV · không sửa đổi',
    desc_en: 'View reports · export CSV · no writes',
  },
];

const ROLE_COL_INDEX: Record<RoleId, number> = { owner: 0, ops: 1, seller: 2, wh: 3, ro: 4 };

type PermCellValue = 'F' | 'V' | '-';

interface PermDef {
  vi: string;
  en: string;
  r: PermCellValue[];
}

interface PermGroup {
  vi: string;
  en: string;
  perms: PermDef[];
}

const PERM_GROUPS: PermGroup[] = [
  {
    vi: 'Tenant',
    en: 'Tenant',
    perms: [
      {
        vi: 'Quản trị workspace settings',
        en: 'Manage workspace settings',
        r: ['F', 'F', '-', '-', '-'],
      },
      { vi: 'Xoá tenant / lưu trữ', en: 'Delete / archive tenant', r: ['F', '-', '-', '-', '-'] },
      { vi: 'Xem audit log', en: 'View audit log', r: ['F', 'F', 'V', '-', 'V'] },
      { vi: 'Xuất dữ liệu tenant', en: 'Export tenant data', r: ['F', 'F', '-', '-', '-'] },
    ],
  },
  {
    vi: 'Thành viên & vai trò',
    en: 'Members & roles',
    perms: [
      { vi: 'Mời / xoá thành viên', en: 'Invite / remove members', r: ['F', 'F', '-', '-', '-'] },
      { vi: 'Đổi vai trò', en: 'Change roles', r: ['F', 'F', '-', '-', '-'] },
      { vi: 'Tạo vai trò tuỳ chỉnh', en: 'Create custom roles', r: ['F', '-', '-', '-', '-'] },
      { vi: 'Reset MFA cho thành viên', en: 'Reset member MFA', r: ['F', 'F', '-', '-', '-'] },
    ],
  },
  {
    vi: 'Tồn kho',
    en: 'Inventory',
    perms: [
      { vi: 'Tạo / sửa SKU', en: 'Create / edit SKUs', r: ['F', 'F', 'F', '-', 'V'] },
      {
        vi: 'Điều chỉnh tồn (số đếm)',
        en: 'Stock adjustments (cycle counts)',
        r: ['F', 'F', 'F', 'F', 'V'],
      },
      { vi: 'Đặt ngưỡng cảnh báo', en: 'Set safety thresholds', r: ['F', 'F', 'F', '-', 'V'] },
      { vi: 'Xem giá vốn', en: 'View cost prices', r: ['F', 'F', 'F', '-', 'V'] },
      { vi: 'Quản lý phân bổ kênh', en: 'Manage channel allocation', r: ['F', 'F', 'F', '-', 'V'] },
    ],
  },
  {
    vi: 'Đơn hàng',
    en: 'Orders',
    perms: [
      { vi: 'Tạo / sửa đơn', en: 'Create / edit orders', r: ['F', 'F', 'F', '-', 'V'] },
      { vi: 'Huỷ đơn', en: 'Cancel orders', r: ['F', 'F', 'F', '-', '-'] },
      { vi: 'Hoàn tiền', en: 'Issue refunds', r: ['F', 'F', 'F', '-', '-'] },
      { vi: 'Nhặt / đóng gói', en: 'Pick / pack', r: ['F', 'F', 'F', 'F', '-'] },
      { vi: 'Bỏ mặt nạ PII khách hàng', en: 'Unmask customer PII', r: ['F', 'F', 'V', '-', '-'] },
    ],
  },
  {
    vi: 'Kênh & API',
    en: 'Channels & API',
    perms: [
      {
        vi: 'Kết nối / ngắt kênh',
        en: 'Connect / disconnect channels',
        r: ['F', 'F', '-', '-', '-'],
      },
      { vi: 'Quay vòng API key', en: 'Rotate API keys', r: ['F', 'F', '-', '-', '-'] },
      { vi: 'Xem webhook payload', en: 'View webhook payloads', r: ['F', 'F', 'V', '-', 'V'] },
    ],
  },
];

function RolesSettings() {
  const [activeRole, setActiveRole] = useState<RoleId>('ops');
  const activeCol = ROLE_COL_INDEX[activeRole];

  return (
    <div className="scroll-y" style={{ flex: 1 }}>
      <SettingsBreadcrumb
        crumb={t('Vai trò & quyền', 'Roles & permissions')}
        sub={`${ROLES.length} ${t('vai trò · 5 mặc định, 0 tuỳ chỉnh', 'roles · 5 default, 0 custom')}`}
      />

      <div data-review="roles" style={{ padding: '14px 24px 40px', maxWidth: 1180 }}>
        {/* Role cards */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: `repeat(${ROLES.length}, 1fr)`,
            gap: 8,
            marginBottom: 18,
          }}
        >
          {ROLES.map((r) => {
            const sel = activeRole === r.id;
            return (
              <button
                key={r.id}
                type="button"
                onClick={() => setActiveRole(r.id)}
                className="nb"
                style={{
                  padding: '12px 12px',
                  border: '1px solid ' + (sel ? 'var(--accent)' : 'var(--line)'),
                  borderTop: sel ? '3px solid var(--accent)' : '1px solid var(--line)',
                  background: sel ? 'var(--accent-soft)' : 'var(--panel)',
                  borderRadius: 3,
                  textAlign: 'left',
                  cursor: 'pointer',
                  minWidth: 0,
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
                  <span style={{ fontSize: 12.5, fontWeight: 600 }}>{r.name}</span>
                  {r.locked && (
                    <Lock
                      size={10}
                      strokeWidth={1.5}
                      style={{ color: 'var(--ink-3)' }}
                      aria-hidden
                    />
                  )}
                </div>
                <div
                  style={{
                    fontSize: 10.5,
                    color: 'var(--ink-3)',
                    marginBottom: 8,
                    lineHeight: 1.4,
                    minHeight: 28,
                  }}
                >
                  {t(r.desc_vi, r.desc_en)}
                </div>
                <div style={{ fontSize: 18, fontWeight: 700, fontFamily: 'var(--font-mono)' }}>
                  {r.count}
                </div>
                <div className="lbl">{t('thành viên', 'members')}</div>
              </button>
            );
          })}
        </div>

        {/* Action bar */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10 }}>
          <div style={{ fontSize: 14, fontWeight: 600 }}>
            {t('Ma trận quyền', 'Permissions matrix')}
          </div>
          <span style={{ flex: 1 }} />
          <PermLegend />
          <button className="btn sm" type="button">
            <Copy size={11} aria-hidden />{' '}
            {t('Sao chép từ vai trò khác', 'Clone from another role')}
          </button>
          <button className="btn sm primary" type="button">
            <Plus size={11} aria-hidden /> {t('Tạo vai trò tuỳ chỉnh', 'Create custom role')}
          </button>
        </div>

        {/* Matrix */}
        <div
          style={{
            border: '1px solid var(--line)',
            borderRadius: 'var(--radius-lg)',
            overflow: 'hidden',
            background: 'var(--panel)',
          }}
        >
          <table className="t-data perm-matrix">
            <thead>
              <tr>
                <th style={{ width: '36%' }}>{t('Quyền', 'Permission')}</th>
                {ROLES.map((r) => (
                  <th
                    key={r.id}
                    style={{
                      textAlign: 'center',
                      background: activeRole === r.id ? 'var(--accent-soft)' : undefined,
                    }}
                  >
                    <div
                      style={{
                        fontSize: 11,
                        fontWeight: 600,
                        color: activeRole === r.id ? 'var(--accent-ink)' : 'var(--ink-2)',
                      }}
                    >
                      {r.name}
                    </div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {PERM_GROUPS.map((g) => (
                <Fragment key={g.en}>
                  <tr className="perm-group-row">
                    <td
                      colSpan={6}
                      style={{
                        background: 'var(--bg-sunken)',
                        fontSize: 10.5,
                        color: 'var(--ink-3)',
                        letterSpacing: '0.08em',
                        textTransform: 'uppercase',
                        fontWeight: 600,
                        padding: '8px 14px',
                      }}
                    >
                      {t(g.vi, g.en)}
                    </td>
                  </tr>
                  {g.perms.map((p) => (
                    <tr key={p.en}>
                      <td style={{ fontSize: 12 }}>{t(p.vi, p.en)}</td>
                      {p.r.map((cell, ci) => (
                        <td
                          key={ci}
                          style={{
                            textAlign: 'center',
                            background: ci === activeCol ? 'var(--accent-soft)' : undefined,
                          }}
                        >
                          <PermCell value={cell} />
                        </td>
                      ))}
                    </tr>
                  ))}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>

        <div
          style={{
            marginTop: 12,
            padding: 12,
            background: 'var(--bg-soft)',
            border: '1px solid var(--line)',
            borderRadius: 3,
            fontSize: 11.5,
            color: 'var(--ink-2)',
            lineHeight: 1.6,
            display: 'flex',
            gap: 10,
            alignItems: 'flex-start',
          }}
        >
          <Info
            size={13}
            strokeWidth={1.5}
            style={{ marginTop: 1, flex: 'none', color: 'var(--ink-3)' }}
            aria-hidden
          />
          <span>
            {t(
              'Mỗi thay đổi quyền được ghi vào audit log với mã idempotency riêng. Owner role không thể giảm xuống dưới 1 người. Khi tạo vai trò tuỳ chỉnh, hệ thống yêu cầu chọn vai trò nguồn để kế thừa.',
              'Every permission change is written to the audit log with its own idempotency key. The Owner role cannot drop below 1 member. Custom roles must select a source role to inherit from.',
            )}
          </span>
        </div>
      </div>
    </div>
  );
}

function PermCell({ value }: { value: PermCellValue }) {
  if (value === 'F') {
    return (
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 18,
          height: 18,
          borderRadius: 9,
          background: 'var(--ok-soft)',
          color: 'var(--ok-ink)',
        }}
        title={t('Toàn quyền', 'Full')}
      >
        <Check size={11} strokeWidth={2.5} aria-hidden />
      </span>
    );
  }
  if (value === 'V') {
    return (
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 18,
          height: 18,
          borderRadius: 9,
          background: 'var(--info-soft)',
          color: 'var(--info-ink, var(--ink-2))',
        }}
        title={t('Chỉ xem', 'View only')}
      >
        <Eye size={11} strokeWidth={2} aria-hidden />
      </span>
    );
  }
  return (
    <span
      style={{ display: 'inline-block', width: 12, height: 1.5, background: 'var(--line-strong)' }}
      title={t('Không có quyền', 'No access')}
    />
  );
}

function PermLegend() {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        fontSize: 11,
        color: 'var(--ink-2)',
      }}
    >
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        <span
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: 14,
            height: 14,
            borderRadius: 7,
            background: 'var(--ok-soft)',
            color: 'var(--ok-ink)',
          }}
        >
          <Check size={9} strokeWidth={2.5} aria-hidden />
        </span>
        {t('toàn quyền', 'full')}
      </span>
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        <span
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: 14,
            height: 14,
            borderRadius: 7,
            background: 'var(--info-soft)',
            color: 'var(--info-ink, var(--ink-2))',
          }}
        >
          <Eye size={9} strokeWidth={2} aria-hidden />
        </span>
        {t('chỉ xem', 'view')}
      </span>
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        <span
          style={{
            display: 'inline-block',
            width: 10,
            height: 1.5,
            background: 'var(--line-strong)',
          }}
        />
        {t('không có', 'none')}
      </span>
    </div>
  );
}

// ── Tier-3 stub ──────────────────────────────────────────────────────────────

function SettingsStub({
  icon: IconCmp,
  title,
  blurb,
  milestones,
}: {
  icon: typeof Lock;
  title: string;
  blurb: string;
  milestones: string[];
}) {
  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
      <SettingsBreadcrumb crumb={title} sub={t('Tier 3 · giữ chỗ', 'Tier 3 · placeholder')} />
      <div
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 40,
        }}
      >
        <div
          style={{
            width: 480,
            maxWidth: '100%',
            padding: 28,
            border: '1px dashed var(--line-strong)',
            borderRadius: 'var(--radius-lg)',
            background: 'var(--panel)',
          }}
        >
          <div
            className="fs0"
            style={{
              width: 44,
              height: 44,
              borderRadius: 4,
              background: 'var(--bg-sunken)',
              border: '1px solid var(--line)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              marginBottom: 12,
            }}
          >
            <IconCmp size={20} strokeWidth={1.5} style={{ color: 'var(--ink-3)' }} aria-hidden />
          </div>
          <div style={{ fontSize: 16, fontWeight: 600 }}>{title}</div>
          <div style={{ fontSize: 12.5, color: 'var(--ink-2)', marginTop: 6, lineHeight: 1.55 }}>
            {blurb}
          </div>
          <div style={{ marginTop: 16, paddingTop: 14, borderTop: '1px solid var(--line)' }}>
            <div className="lbl" style={{ marginBottom: 8 }}>
              {t('Lộ trình', 'Roadmap')}
            </div>
            <ul
              style={{
                margin: 0,
                padding: 0,
                listStyle: 'none',
                display: 'flex',
                flexDirection: 'column',
                gap: 6,
              }}
            >
              {milestones.map((m) => (
                <li
                  key={m}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                    fontSize: 11.5,
                    color: 'var(--ink-2)',
                  }}
                >
                  <span
                    style={{ width: 5, height: 5, borderRadius: 2.5, background: 'var(--ink-4)' }}
                  />
                  {m}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
