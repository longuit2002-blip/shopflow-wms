import { useState } from 'react';
import { createFileRoute, Link } from '@tanstack/react-router';
import { Search, Download, UserPlus, ChevronRight } from 'lucide-react';
import { Pill, type PillKind } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Tenants — the platform-admin tenant-roster screen (design-handoff
 * `TenantsAdmin`).
 *
 * Ported from the design handoff `app.jsx` (`TenantsAdmin` + `TENANT_ROWS` +
 * `TENANT_STATE_META`). A `.strip` header with summary Stats
 * (active / trial / provisioning / attention counts), a `.hairline-b` filter
 * bar (search + per-state filter buttons + Export CSV + Onboard tenant), and a
 * `.t-data` table of ~12 mock tenants — legal entity, ERC number, region, the
 * per-tenant `shopflow_<slug>` database name (the database-per-tenant
 * isolation wedge), p99 reserve latency, a SKU/cap progress bar, and a status
 * pill.
 *
 * Deviations from the handoff component:
 * - The design's `role` prop + manager-only lockout panel are DROPPED. This
 *   surface is permission-gated at the nav level (the Tenants nav item
 *   requires `auth.admin.users.list`), so the table renders directly with no
 *   `role` prop.
 * - "Onboard tenant" links to the real `/onboarding` route via TanStack
 *   `<Link>` rather than invoking an `onOnboard` callback.
 * - The design's standalone `Stat` / `Bar` helpers become a local `TenantStat`
 *   (matching the settings.tsx `MemberStat` shape) and an inline `.bar`
 *   render (matching channels.tsx) — no new components invented.
 *
 * Data is mocked in the frontend (no tenant-admin read endpoints are wired to
 * this surface yet). `data-review` / `data-tour` anchors preserved from the
 * handoff (QA + guided-tour contract).
 */

// ── Mock tenant roster ───────────────────────────────────────────────────────

type TenantState = 'active' | 'at-cap' | 'trial' | 'provisioning' | 'degraded' | 'suspended';

interface TenantRow {
  legal: string;
  erc: string;
  region: string;
  p99: number;
  sku: number;
  cap: number;
  state: TenantState;
}

const TENANT_ROWS: TenantRow[] = [
  {
    legal: 'Công ty Cổ phần Mỹ Phẩm Sao Mai',
    erc: '0312445678',
    region: 'HCMC',
    p99: 142,
    sku: 2100,
    cap: 5000,
    state: 'active',
  },
  {
    legal: 'Nhà Thuốc Nam Dược TNHH',
    erc: '0309887123',
    region: 'Hà Nội',
    p99: 186,
    sku: 4350,
    cap: 5000,
    state: 'active',
  },
  {
    legal: 'Công ty TNHH Thủy Hải Sản Đà Nẵng',
    erc: '0401556782',
    region: 'Đà Nẵng',
    p99: 211,
    sku: 1200,
    cap: 5000,
    state: 'active',
  },
  {
    legal: 'Công ty Cổ phần Thời Trang Viễn Đông',
    erc: '0316771204',
    region: 'HCMC',
    p99: 168,
    sku: 3050,
    cap: 5000,
    state: 'active',
  },
  {
    legal: 'Nestlé Việt Nam · mid-market pilot',
    erc: '0301123456',
    region: 'HCMC',
    p99: 224,
    sku: 4870,
    cap: 5000,
    state: 'at-cap',
  },
  {
    legal: 'Công ty TNHH Mẹ và Bé Beebay',
    erc: '0314009912',
    region: 'Hà Nội',
    p99: 0,
    sku: 0,
    cap: 5000,
    state: 'provisioning',
  },
  {
    legal: 'Công ty TNHH Nhà Sách Trí Việt',
    erc: '0308220471',
    region: 'HCMC',
    p99: 0,
    sku: 0,
    cap: 5000,
    state: 'provisioning',
  },
  {
    legal: 'Công ty Cổ phần Nông Sản Lam Sa',
    erc: '0402889561',
    region: 'Miền Tây',
    p99: 154,
    sku: 980,
    cap: 5000,
    state: 'trial',
  },
  {
    legal: 'Saigon Coffee Roasters TNHH',
    erc: '0317994022',
    region: 'HCMC',
    p99: 137,
    sku: 412,
    cap: 5000,
    state: 'trial',
  },
  {
    legal: 'Công ty Gia Dụng Long Việt',
    erc: '0306554981',
    region: 'Bình Dương',
    p99: 295,
    sku: 2870,
    cap: 5000,
    state: 'degraded',
  },
  {
    legal: 'Công ty TNHH Thể Thao Hồng Hà',
    erc: '0310445782',
    region: 'Hà Nội',
    p99: 0,
    sku: 3120,
    cap: 5000,
    state: 'suspended',
  },
  {
    legal: 'Công ty TNHH Quà Tặng Tinh Hoa',
    erc: '0305667123',
    region: 'Huế',
    p99: 0,
    sku: 540,
    cap: 5000,
    state: 'suspended',
  },
];

interface StateMeta {
  vi: string;
  en: string;
  kind: PillKind;
}

// `kind: 'neutral'` in the design maps to the `default` PillKind here.
const TENANT_STATE_META: Record<TenantState, StateMeta> = {
  active: { vi: 'hoạt động', en: 'active', kind: 'ok' },
  'at-cap': { vi: 'chạm trần', en: 'at SKU cap', kind: 'warn' },
  trial: { vi: 'dùng thử', en: 'trial', kind: 'info' },
  provisioning: { vi: 'khởi tạo', en: 'provisioning', kind: 'info' },
  degraded: { vi: 'suy giảm', en: 'degraded', kind: 'bad' },
  suspended: { vi: 'tạm dừng', en: 'suspended', kind: 'default' },
};

const FALLBACK_META: StateMeta = { vi: '—', en: '—', kind: 'default' };

// TS-strict (noUncheckedIndexedAccess) safe lookup — always returns a defined meta.
function metaFor(state: TenantState): StateMeta {
  return TENANT_STATE_META[state] ?? FALLBACK_META;
}

function countBy(state: TenantState): number {
  return TENANT_ROWS.filter((r) => r.state === state).length;
}

// `shopflow_<slug>` — the per-tenant database name, the isolation wedge.
function dbNameFor(legal: string): string {
  return (
    'shopflow_' +
    legal
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '')
      .slice(0, 26)
  );
}

type StateFilter = 'all' | TenantState;

const STATE_FILTERS: StateFilter[] = [
  'all',
  'active',
  'at-cap',
  'trial',
  'provisioning',
  'degraded',
  'suspended',
];

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/tenants')({
  component: TenantsRouteComponent,
});

function TenantsRouteComponent() {
  useLocale();
  const [stateFilter, setStateFilter] = useState<StateFilter>('all');
  const [q, setQ] = useState('');

  const query = q.trim().toLowerCase();
  const filtered = TENANT_ROWS.filter((r) => {
    if (stateFilter !== 'all' && r.state !== stateFilter) return false;
    if (query && !(r.legal.toLowerCase().includes(query) || r.erc.includes(q.trim()))) return false;
    return true;
  });

  const attention = countBy('at-cap') + countBy('degraded') + countBy('suspended');

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <div className="strip">
        <span className="t">
          {t('Tenants', 'Tenants')} · {TENANT_ROWS.length}
        </span>
        <Pill kind="info">{t('platform admin', 'platform admin')}</Pill>
        <span style={{ flex: 1 }} />
        <TenantStat
          label={t('Hiệu lực', 'Active')}
          value={countBy('active')}
          sub={t('sản xuất', 'production')}
          kind="ok"
        />
        <span style={{ width: 24 }} />
        <TenantStat
          label={t('Dùng thử', 'Trial')}
          value={countBy('trial')}
          sub={t('30 ngày', '30-day')}
          kind="info"
        />
        <span style={{ width: 24 }} />
        <TenantStat
          label={t('Khởi tạo', 'Provisioning')}
          value={countBy('provisioning')}
          sub={t('đang chạy', 'running')}
          kind="info"
        />
        <span style={{ width: 24 }} />
        <TenantStat
          label={t('Chú ý', 'Attention')}
          value={attention}
          sub={t('cap / degraded / suspended', 'cap / degraded / suspended')}
          kind="warn"
        />
      </div>

      <div
        className="hairline-b"
        data-tour="tenants-filter"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          rowGap: 8,
          padding: '10px 18px',
          alignItems: 'center',
          background: 'var(--bg-soft)',
        }}
      >
        <div style={{ position: 'relative', flex: '0 0 280px' }}>
          <Search
            size={13}
            strokeWidth={1.5}
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
            placeholder={t('Tìm theo tên pháp lý hoặc ERC…', 'Legal name or ERC number…')}
            style={{ paddingLeft: 26, width: '100%' }}
            value={q}
            onChange={(e) => setQ(e.target.value)}
            aria-label={t('Tìm tenant', 'Search tenants')}
          />
        </div>
        <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
          {STATE_FILTERS.map((s) => {
            const active = stateFilter === s;
            const label = s === 'all' ? t('Tất cả', 'All') : t(metaFor(s).vi, metaFor(s).en);
            const count = s === 'all' ? TENANT_ROWS.length : countBy(s);
            return (
              <button
                key={s}
                className={'btn sm' + (active ? ' primary' : '')}
                type="button"
                onClick={() => setStateFilter(s)}
                style={{ height: 28 }}
              >
                {label}
                <span style={{ marginLeft: 6, fontSize: 10.5, opacity: 0.75 }}>{count}</span>
              </button>
            );
          })}
        </div>
        <span style={{ flex: 1 }} />
        <button className="btn sm" type="button">
          <Download size={11} strokeWidth={1.5} aria-hidden /> {t('Xuất CSV', 'Export CSV')}
        </button>
        <Link to="/onboarding" className="btn sm primary" style={{ textDecoration: 'none' }}>
          <UserPlus size={11} strokeWidth={1.5} aria-hidden />{' '}
          {t('Khởi tạo tenant', 'Onboard tenant')}
        </Link>
      </div>

      <div className="scroll-y" style={{ flex: 1 }}>
        <table className="t-data" data-review="tenant-table">
          <thead>
            <tr>
              <th>{t('Pháp nhân', 'Legal entity')}</th>
              <th style={{ width: 110 }}>{t('Số đăng ký', 'ERC number')}</th>
              <th style={{ width: 100 }}>{t('Khu vực', 'Region')}</th>
              <th>Database</th>
              <th style={{ width: 90, textAlign: 'right' }}>{t('p99 giữ chỗ', 'p99 reserve')}</th>
              <th style={{ width: 140 }}>{t('SKU / trần', 'SKU / cap')}</th>
              <th style={{ width: 110 }}>{t('Trạng thái', 'Status')}</th>
              <th style={{ width: 24 }} aria-label={t('Chi tiết', 'Detail')} />
            </tr>
          </thead>
          <tbody>
            {filtered.map((tnt) => {
              const meta = metaFor(tnt.state);
              const provisioning = tnt.state === 'provisioning';
              const pct = Math.round((tnt.sku / tnt.cap) * 100);
              const barKind = pct > 90 ? 'bad' : pct > 80 ? 'warn' : 'ok';
              return (
                <tr key={tnt.erc} style={{ cursor: 'pointer' }}>
                  <td>
                    <span style={{ fontWeight: 500 }}>{tnt.legal}</span>
                  </td>
                  <td className="mono">{tnt.erc}</td>
                  <td>{tnt.region}</td>
                  <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-2)' }}>
                    {provisioning ? (
                      <span style={{ color: 'var(--ink-3)' }}>{t('đang tạo…', 'creating…')}</span>
                    ) : (
                      dbNameFor(tnt.legal)
                    )}
                  </td>
                  <td
                    className="mono tnum"
                    style={{
                      textAlign: 'right',
                      color:
                        tnt.p99 === 0
                          ? 'var(--ink-4)'
                          : tnt.p99 > 250
                            ? 'var(--bad-ink)'
                            : 'var(--ink)',
                    }}
                  >
                    {tnt.p99 === 0 ? '—' : tnt.p99 + 'ms'}
                  </td>
                  <td>
                    {tnt.sku === 0 ? (
                      <span style={{ color: 'var(--ink-4)', fontSize: 11.5 }}>
                        {t('chưa có dữ liệu', 'no data yet')}
                      </span>
                    ) : (
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <div className={'bar ' + barKind} style={{ width: 60 }}>
                          <i style={{ width: Math.min(100, Math.max(0, pct)) + '%' }} />
                        </div>
                        <span className="mono tnum" style={{ fontSize: 11.5 }}>
                          {tnt.sku.toLocaleString()}/{tnt.cap / 1000}K
                        </span>
                      </div>
                    )}
                  </td>
                  <td>
                    <Pill kind={meta.kind}>{t(meta.vi, meta.en)}</Pill>
                  </td>
                  <td>
                    <ChevronRight size={12} style={{ color: 'var(--ink-4)' }} aria-hidden />
                  </td>
                </tr>
              );
            })}
            {filtered.length === 0 && (
              <tr>
                <td colSpan={8} style={{ textAlign: 'center', padding: 40, color: 'var(--ink-3)' }}>
                  {t('Không có tenant nào khớp bộ lọc.', 'No tenants match the filter.')}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Shared bits ──────────────────────────────────────────────────────────────

/**
 * Strip summary stat — the design's `Stat` helper, modelled on the proven
 * settings.tsx `MemberStat` shape (`.lbl` + `.mono.tnum` value + sub).
 */
function TenantStat({
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
      ? 'var(--ok)'
      : kind === 'warn'
        ? 'var(--warn-ink)'
        : kind === 'bad'
          ? 'var(--bad-ink)'
          : kind === 'info'
            ? 'var(--info-ink, var(--ink-2))'
            : 'var(--ink)';
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 0 }}>
      <span className="lbl">{label}</span>
      <span
        className="mono tnum"
        style={{ fontSize: 18, fontWeight: 600, color, lineHeight: 1.1, letterSpacing: '-0.01em' }}
      >
        {value}
      </span>
      <span style={{ fontSize: 11, color: 'var(--ink-3)' }}>{sub}</span>
    </div>
  );
}
