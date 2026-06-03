import { Fragment, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import {
  Plus,
  Plug,
  RotateCw,
  Settings,
  Unlink,
  Info,
  Sparkles,
  MoreHorizontal,
  ChevronRight,
  ChevronLeft,
  ShieldCheck,
  Download,
  Check,
  X,
} from 'lucide-react';
import { Pill } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Channels — the marketplace-connection control surface (design-handoff
 * screen #3).
 *
 * Ported from the design handoff `screen-channels.jsx`. Header strip → tab
 * bar (Overview · Allocation rules · Webhook log · Compliance) over a
 * scroll body, plus a 3-step Connect-channel modal flow (OAuth handshake →
 * product mapping → confirm).
 *
 * - Overview: per-channel cards with last-sync, orders-today, token-bucket
 *   headroom bar, circuit-breaker state, last webhook, and a PDPA
 *   sub-processor footer — the four SEA marketplaces (Shopee / Lazada /
 *   TikTok Shop / Shopify) plus one available-to-connect dashed card.
 * - Allocation rules: per-channel weight sliders with priority / cap /
 *   safety-stock columns and a live 1.000-unit split preview.
 * - Webhook log: live event feed with idempotency dedup state, latency, and
 *   tenant routing.
 * - Compliance: sub-processor disclosure table + recent audit trail.
 *
 * Data is mocked in the frontend (no channel-admin read endpoints are wired
 * to this surface yet). `data-review` / `data-tour` anchors preserved from
 * the handoff (QA + guided-tour contract). `Math.random()` from the design
 * is replaced with deterministic mock fields — committed source stays
 * stable across renders.
 */

// ── Mock channels + tenant ──────────────────────────────────────────────────

type ChannelId = 'shopee' | 'lazada' | 'tiktok' | 'shopify' | 'sendo';

interface Channel {
  id: ChannelId;
  name: string;
  accountTag: string;
  color: string;
  short: string;
}

const CHANNELS: Channel[] = [
  {
    id: 'shopee',
    name: 'Shopee Mall',
    accountTag: 'YenSaoKH',
    color: 'var(--ch-shopee)',
    short: 'Shopee',
  },
  {
    id: 'lazada',
    name: 'Lazada LazMall',
    accountTag: 'yensao_kh',
    color: 'var(--ch-lazada)',
    short: 'Lazada',
  },
  {
    id: 'tiktok',
    name: 'TikTok Shop',
    accountTag: 'yensaokh.vn',
    color: 'var(--ch-tiktok)',
    short: 'TikTok',
  },
  {
    id: 'shopify',
    name: 'Shopify',
    accountTag: 'yensaokhanhhoa.myshopify.com',
    color: 'var(--ch-shopify)',
    short: 'Shopify',
  },
];

const SENDO: Channel = {
  id: 'sendo',
  name: 'Sendo',
  accountTag: '',
  color: 'var(--ch-sendo)',
  short: 'Sendo',
};

const UNKNOWN_CHANNEL: Channel = {
  id: 'sendo',
  name: '—',
  accountTag: '',
  color: 'var(--ink-3)',
  short: '—',
};

function chBy(id: string): Channel {
  return CHANNELS.find((c) => c.id === id) ?? (id === 'sendo' ? SENDO : UNKNOWN_CHANNEL);
}

const TENANT_DB = 'shopflow_yensaokhanhhoa';

type BreakerState = 'closed' | 'half-open' | 'open';

interface ChannelStrip {
  id: ChannelId;
  ordersToday: number;
  syncMs: number;
  rate: number;
  breaker: BreakerState;
  lastSync: string;
  err24: string;
}

const CHANNEL_STRIP: ChannelStrip[] = [
  {
    id: 'shopee',
    ordersToday: 542,
    syncMs: 18,
    rate: 0.86,
    breaker: 'closed',
    lastSync: '4s',
    err24: '0.2%',
  },
  {
    id: 'lazada',
    ordersToday: 388,
    syncMs: 22,
    rate: 0.71,
    breaker: 'closed',
    lastSync: '7s',
    err24: '0.4%',
  },
  {
    id: 'tiktok',
    ordersToday: 691,
    syncMs: 14,
    rate: 0.92,
    breaker: 'closed',
    lastSync: '2s',
    err24: '0.1%',
  },
  {
    id: 'shopify',
    ordersToday: 102,
    syncMs: 26,
    rate: 0.94,
    breaker: 'half-open',
    lastSync: '38s',
    err24: '0.6%',
  },
];

const FALLBACK_STRIP: ChannelStrip = {
  id: 'shopee',
  ordersToday: 0,
  syncMs: 0,
  rate: 1,
  breaker: 'closed',
  lastSync: '—',
  err24: '0.0%',
};

function stripFor(id: ChannelId): ChannelStrip {
  return CHANNEL_STRIP.find((s) => s.id === id) ?? FALLBACK_STRIP;
}

const VI_NUM = new Intl.NumberFormat('vi-VN');

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/channels')({
  component: ChannelsRouteComponent,
});

type TabKey = 'overview' | 'allocation' | 'webhooks' | 'compliance';

function ChannelsRouteComponent() {
  useLocale();
  const [tab, setTab] = useState<TabKey>('overview');
  const [connectOpen, setConnectOpen] = useState(false);

  const tabs: [TabKey, string][] = [
    ['overview', t('Tổng quan', 'Overview')],
    ['allocation', t('Quy tắc phân bổ', 'Allocation rules')],
    ['webhooks', t('Nhật ký webhook', 'Webhook log')],
    ['compliance', t('Tuân thủ', 'Compliance')],
  ];

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <div className="strip">
        <span className="t">{t('Kênh bán', 'Channels')}</span>
        <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
          ·{' '}
          {t(
            '4 đã kết nối · 1 có thể thêm · công bố sub-processor đang hiệu lực',
            '4 connected · 1 available · sub-processor disclosure in effect',
          )}
        </span>
        <Pill kind="ok">{t('lưu trữ dữ liệu · SG-1', 'data residency · SG-1')}</Pill>
        <span style={{ flex: 1 }} />
        <button className="btn sm" type="button" onClick={() => setConnectOpen(true)}>
          <Plus size={11} strokeWidth={1.5} aria-hidden />{' '}
          {t('Kết nối kênh mới', 'Connect channel')}
        </button>
      </div>

      <div
        className="hairline-b"
        data-tour="channels-tabs"
        style={{ display: 'flex', padding: '0 18px', background: 'var(--panel-2)' }}
      >
        {tabs.map(([k, l]) => (
          <button
            key={k}
            type="button"
            onClick={() => setTab(k)}
            style={{
              border: 'none',
              background: 'transparent',
              padding: '12px 4px',
              marginRight: 18,
              color: tab === k ? 'var(--ink)' : 'var(--ink-3)',
              borderBottom: tab === k ? '2px solid var(--accent)' : '2px solid transparent',
              fontSize: 12.5,
              fontWeight: tab === k ? 600 : 500,
              cursor: 'pointer',
            }}
          >
            {l}
          </button>
        ))}
      </div>

      <div className="scroll-y" style={{ flex: 1 }}>
        {tab === 'overview' && <ChannelCards />}
        {tab === 'allocation' && <AllocationRules />}
        {tab === 'webhooks' && <WebhookLog />}
        {tab === 'compliance' && <ComplianceTab />}
      </div>

      {connectOpen && <ConnectChannelFlow onClose={() => setConnectOpen(false)} />}
    </div>
  );
}

// ── Overview tab ─────────────────────────────────────────────────────────────

function ChannelCards() {
  return (
    <div
      data-review="channel-cards"
      style={{ padding: 18, display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 14 }}
    >
      {CHANNELS.map((c) => (
        <ChannelCard key={c.id} channel={c} />
      ))}

      {/* Available to connect */}
      <div
        className="card"
        style={{ overflow: 'hidden', borderStyle: 'dashed', borderColor: 'var(--line-2)' }}
      >
        <div style={{ padding: '12px 16px', display: 'flex', alignItems: 'center', gap: 10 }}>
          <span
            className="oauth-mark"
            style={{
              background: 'var(--bg-sunken)',
              color: 'var(--ink-3)',
              border: '1px solid var(--line)',
            }}
          >
            S
          </span>
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 13.5, fontWeight: 600 }}>Sendo</div>
            <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              {t(
                'Có sẵn — kết nối để mở rộng sang kênh thứ 5',
                'Available — connect to add a 5th channel',
              )}
            </div>
          </div>
          <button className="btn sm primary" type="button">
            <Plug size={11} strokeWidth={1.5} aria-hidden /> {t('Kết nối', 'Connect')}
          </button>
        </div>
      </div>
    </div>
  );
}

function ChannelCard({ channel }: { channel: Channel }) {
  const s = stripFor(channel.id);
  const connected = channel.id !== 'shopify';
  const tokensLeft = Math.round(s.rate * 5000);
  const rateKind = s.rate < 0.5 ? 'bad' : s.rate < 0.75 ? 'warn' : 'ok';
  const breakerClosed = s.breaker === 'closed';

  return (
    <div className="card" style={{ overflow: 'hidden', position: 'relative' }}>
      <div
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          height: 4,
          background: channel.color,
        }}
      />
      <div
        style={{
          padding: '14px 16px 12px',
          borderBottom: '1px solid var(--line)',
          display: 'flex',
          alignItems: 'center',
          gap: 10,
        }}
      >
        <span className="oauth-mark" style={{ background: channel.color }}>
          {channel.short.charAt(0)}
        </span>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 13.5, fontWeight: 600 }}>{channel.name}</div>
          <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
            {channel.accountTag}
          </div>
        </div>
        <Pill kind={connected ? 'ok' : 'warn'}>
          <span
            className="s-dot"
            style={{ background: connected ? 'var(--ok)' : 'var(--warn)', marginRight: 4 }}
          />
          {connected ? t('đã kết nối', 'connected') : t('giảm hiệu suất', 'degraded')}
        </Pill>
      </div>

      <div
        style={{ padding: '12px 16px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}
      >
        <div>
          <div className="lbl">{t('Đồng bộ thành công gần nhất', 'Last successful sync')}</div>
          <div className="mono tnum" style={{ fontSize: 14, fontWeight: 600, marginTop: 2 }}>
            {s.lastSync} {t('trước', 'ago')}
          </div>
          <div style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
            {t('p99 giữ chỗ', 'p99 reserve')} {s.syncMs}ms
          </div>
        </div>
        <div>
          <div className="lbl">{t('Đơn hôm nay', 'Orders today')}</div>
          <div className="mono tnum" style={{ fontSize: 14, fontWeight: 600, marginTop: 2 }}>
            {VI_NUM.format(s.ordersToday)}
          </div>
          <div style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
            {t('tỷ lệ lỗi 24h', '24h error rate')} · {s.err24}
          </div>
        </div>

        <div style={{ gridColumn: '1 / -1', marginTop: 4 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
            <span className="lbl">
              {t('Dư địa rate-limit · token bucket', 'Rate-limit headroom · token bucket')}
            </span>
            <span style={{ flex: 1 }} />
            <span className="mono tnum" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              {Math.round(s.rate * 100)}% {t('còn', 'left')} · {tokensLeft}/5000 token
            </span>
          </div>
          <div className={'bar ' + rateKind} style={{ width: '100%' }}>
            <i style={{ width: Math.round(s.rate * 100) + '%' }} />
          </div>
        </div>

        <div>
          <div className="lbl">Circuit breaker</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 2 }}>
            <span className={'s-dot ' + (breakerClosed ? 'ok' : 'warn')} />
            <span className="mono" style={{ fontSize: 12.5, fontWeight: 500 }}>
              {s.breaker}
            </span>
            {!breakerClosed && (
              <span style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
                · {t('thử lại sau 12s', 'retry in 12s')}
              </span>
            )}
          </div>
        </div>
        <div>
          <div className="lbl">{t('Webhook gần nhất', 'Last webhook')}</div>
          <div className="mono" style={{ fontSize: 12.5, fontWeight: 500, marginTop: 2 }}>
            order.created
          </div>
          <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
            {s.lastSync} {t('trước · đã khử trùng', 'ago · deduplicated')}
          </div>
        </div>
      </div>

      {/* Compliance footer */}
      <div
        style={{
          padding: '10px 16px',
          background: 'var(--bg-soft)',
          borderTop: '1px solid var(--line)',
          display: 'grid',
          gridTemplateColumns: 'repeat(3, 1fr)',
          gap: 12,
        }}
      >
        <div>
          <div className="lbl" style={{ fontSize: 9.5 }}>
            Sub-processor
          </div>
          <div style={{ fontSize: 11, marginTop: 2 }}>
            {channel.name} API ·{' '}
            {channel.id === 'shopify'
              ? t('edge toàn cầu', 'global edge')
              : t('khu vực SG', 'SG region')}
          </div>
        </div>
        <div>
          <div className="lbl" style={{ fontSize: 9.5 }}>
            {t('Lưu trữ dữ liệu', 'Data residency')}
          </div>
          <div style={{ fontSize: 11, marginTop: 2 }}>SG-1 · ap-southeast-1</div>
        </div>
        <div>
          <div className="lbl" style={{ fontSize: 9.5 }}>
            {t('Công bố cập nhật', 'Disclosure updated')}
          </div>
          <div className="mono" style={{ fontSize: 11, marginTop: 2 }}>
            2026-03-04
          </div>
        </div>
      </div>

      <div
        style={{ padding: '8px 16px', display: 'flex', gap: 8, borderTop: '1px solid var(--line)' }}
      >
        <button className="btn sm" type="button">
          <RotateCw size={11} strokeWidth={1.5} aria-hidden /> {t('Ép đồng bộ', 'Force sync')}
        </button>
        <button className="btn sm" type="button">
          <Settings size={11} strokeWidth={1.5} aria-hidden /> {t('Cấu hình', 'Configure')}
        </button>
        <span style={{ flex: 1 }} />
        <button className="btn sm ghost" type="button" style={{ color: 'var(--bad-ink)' }}>
          <Unlink size={11} strokeWidth={1.5} aria-hidden /> {t('Ngắt kết nối', 'Disconnect')}
        </button>
      </div>
    </div>
  );
}

// ── Allocation rules tab ──────────────────────────────────────────────────────

interface AllocRule {
  ch: ChannelId;
  weight: number;
  priority: number;
  maxCap: number;
  safety: number;
}

const INITIAL_RULES: AllocRule[] = [
  { ch: 'shopee', weight: 40, priority: 1, maxCap: 200, safety: 20 },
  { ch: 'tiktok', weight: 30, priority: 2, maxCap: 150, safety: 15 },
  { ch: 'lazada', weight: 20, priority: 3, maxCap: 100, safety: 10 },
  { ch: 'shopify', weight: 10, priority: 4, maxCap: 80, safety: 10 },
];

const HYPOTHETICAL_UNITS = 1000;

function AllocationRules() {
  const [rules, setRules] = useState<AllocRule[]>(INITIAL_RULES);

  const totalWeight = rules.reduce((n, r) => n + r.weight, 0) || 1;
  const reserved = rules.reduce((n, r) => n + r.safety, 0);
  // Per-row derived split — paired with its rule so no separate index lookup.
  const splitRows = rules.map((r) => ({
    rule: r,
    units: Math.min(r.maxCap, Math.floor((r.weight / totalWeight) * HYPOTHETICAL_UNITS)),
  }));

  const setWeight = (ch: ChannelId, weight: number) => {
    setRules((prev) => prev.map((r) => (r.ch === ch ? { ...r, weight } : r)));
  };

  return (
    <div
      style={{ padding: 18, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 380px', gap: 14 }}
    >
      <div className="card" data-review="allocation-rules">
        <div className="card-h">
          <span className="t">
            {t('Quy tắc phân bổ · theo danh mục', 'Allocation rules · by category')}
          </span>
          <select
            style={{ marginLeft: 8, fontSize: 12, height: 24 }}
            aria-label={t('Danh mục áp dụng', 'Applied category')}
          >
            <option>{t('Yến sào (tất cả SKU)', "Bird's nest (all SKUs)")}</option>
            <option>{t('Quần áo (tất cả SKU)', 'Apparel (all SKUs)')}</option>
            <option>{t('Cà phê (tất cả SKU)', 'Coffee (all SKUs)')}</option>
            <option>{t('Tùy chỉnh theo SKU…', 'Custom per SKU…')}</option>
          </select>
          <span style={{ flex: 1 }} />
          <Pill kind="info">{t('xem trước trực tiếp', 'live preview')}</Pill>
        </div>

        <table className="t-data">
          <thead>
            <tr>
              <th>{t('Kênh', 'Channel')}</th>
              <th style={{ width: 130 }}>{t('Trọng số', 'Weight')}</th>
              <th style={{ width: 80, textAlign: 'right' }}>{t('Ưu tiên', 'Priority')}</th>
              <th style={{ width: 100, textAlign: 'right' }}>{t('Trần tối đa', 'Max cap')}</th>
              <th style={{ width: 100, textAlign: 'right' }}>{t('Dự phòng', 'Safety')}</th>
              <th style={{ width: 60 }} aria-label={t('Hành động', 'Actions')} />
            </tr>
          </thead>
          <tbody>
            {rules.map((r) => (
              <tr key={r.ch}>
                <td>
                  <ChannelDot id={r.ch} />
                </td>
                <td>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <input
                      type="range"
                      min={0}
                      max={100}
                      value={r.weight}
                      onChange={(e) => setWeight(r.ch, Number.parseInt(e.target.value, 10))}
                      style={{ flex: 1, accentColor: 'var(--accent)' }}
                      aria-label={t('Trọng số', 'Weight') + ' ' + chBy(r.ch).short}
                    />
                    <span className="mono tnum" style={{ width: 36, textAlign: 'right' }}>
                      {r.weight}%
                    </span>
                  </div>
                </td>
                <td className="mono tnum" style={{ textAlign: 'right' }}>
                  {r.priority}
                </td>
                <td className="mono tnum" style={{ textAlign: 'right' }}>
                  {r.maxCap}
                </td>
                <td className="mono tnum" style={{ textAlign: 'right' }}>
                  {r.safety}
                </td>
                <td>
                  <button
                    className="btn ghost sm"
                    type="button"
                    aria-label={t('Tùy chọn', 'Options') + ' ' + chBy(r.ch).short}
                  >
                    <MoreHorizontal size={12} aria-hidden />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <div
          style={{
            padding: '12px 14px',
            background: 'var(--bg-soft)',
            borderTop: '1px solid var(--line)',
            display: 'flex',
            gap: 16,
            fontSize: 11.5,
            color: 'var(--ink-3)',
            alignItems: 'center',
          }}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
            <Info size={11} strokeWidth={1.5} aria-hidden />{' '}
            {t(
              'Trọng số tự cân bằng khi tổng ≠ 100% · weights re-normalize on save',
              'Weights re-normalize on save when the total ≠ 100%',
            )}
          </span>
          <span style={{ flex: 1 }} />
          <button className="btn sm" type="button">
            {t('Khôi phục mặc định', 'Reset to default')}
          </button>
          <button className="btn sm primary" type="button">
            {t('Áp dụng', 'Apply')}
          </button>
        </div>
      </div>

      {/* Live preview */}
      <div className="card">
        <div className="card-h">
          <span className="t">
            {t('Xem trước · 1.000 đơn vị giả định', 'Preview · 1,000 hypothetical units')}
          </span>
        </div>
        <div style={{ padding: 14 }}>
          <div
            style={{
              height: 22,
              display: 'flex',
              borderRadius: 2,
              overflow: 'hidden',
              border: '1px solid var(--line)',
            }}
          >
            {splitRows.map(({ rule, units }) => (
              <div
                key={rule.ch}
                style={{
                  background: chBy(rule.ch).color,
                  flex: units,
                  color: 'white',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: 10.5,
                  fontWeight: 600,
                }}
              >
                {units > 60 && units}
              </div>
            ))}
            <div
              style={{
                background: 'var(--bg-sunken)',
                flex: reserved,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 10.5,
                color: 'var(--ink-3)',
              }}
            >
              {reserved > 40 && `${t('dự phòng', 'safety')} ${reserved}`}
            </div>
          </div>

          <div style={{ marginTop: 14 }}>
            {splitRows.map(({ rule, units }) => (
              <div
                key={rule.ch}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  padding: '6px 0',
                  borderBottom: '1px solid var(--line)',
                }}
              >
                <ChannelDot id={rule.ch} />
                <span style={{ flex: 1 }} />
                <span className="mono tnum" style={{ fontSize: 13, fontWeight: 600 }}>
                  {units}
                </span>
                <span style={{ fontSize: 11, color: 'var(--ink-3)', width: 50 }}>
                  {t('đơn vị', 'units')}
                </span>
              </div>
            ))}
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 0' }}>
              <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
                {t('Dự phòng an toàn', 'Safety stock')}
              </span>
              <span style={{ flex: 1 }} />
              <span
                className="mono tnum"
                style={{ fontSize: 13, fontWeight: 600, color: 'var(--ink-3)' }}
              >
                {reserved}
              </span>
              <span style={{ fontSize: 11, color: 'var(--ink-3)', width: 50 }}>
                {t('đơn vị', 'units')}
              </span>
            </div>
          </div>

          <div
            style={{
              marginTop: 14,
              padding: 10,
              background: 'var(--accent-soft)',
              border: '1px solid var(--accent-line)',
              borderRadius: 3,
              fontSize: 11.5,
              color: 'var(--accent-ink)',
              display: 'flex',
              gap: 6,
              alignItems: 'flex-start',
            }}
          >
            <Sparkles
              size={11}
              strokeWidth={1.5}
              style={{ marginTop: 2, flex: 'none' }}
              aria-hidden
            />
            <span>
              {t(
                'Trong dịp 11.11 / 12.12, quy tắc tự động tăng dự phòng lên 2× và ưu tiên Shopee Mall theo SLA đối tác.',
                'During 11.11 / 12.12, the rules auto-double safety stock and prioritise Shopee Mall per partner SLA.',
              )}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Webhook log tab ────────────────────────────────────────────────────────

interface WebhookEvent {
  ts: string;
  ch: ChannelId;
  event: string;
  idem: 'new' | 'dedup';
  ms: number;
}

const WEBHOOKS: WebhookEvent[] = [
  { ts: '14:02:18.244', ch: 'shopee', event: 'order.created', idem: 'new', ms: 18 },
  { ts: '14:02:17.918', ch: 'tiktok', event: 'order.updated', idem: 'dedup', ms: 14 },
  { ts: '14:02:17.412', ch: 'lazada', event: 'order.created', idem: 'new', ms: 23 },
  { ts: '14:02:16.890', ch: 'shopee', event: 'inventory.synced', idem: 'new', ms: 41 },
  { ts: '14:02:16.211', ch: 'tiktok', event: 'order.created', idem: 'new', ms: 12 },
  { ts: '14:02:15.733', ch: 'shopify', event: 'order.fulfilled', idem: 'new', ms: 31 },
  { ts: '14:02:15.018', ch: 'shopee', event: 'order.cancelled', idem: 'new', ms: 19 },
  { ts: '14:02:14.682', ch: 'lazada', event: 'order.created', idem: 'dedup', ms: 11 },
  { ts: '14:02:14.012', ch: 'tiktok', event: 'product.updated', idem: 'new', ms: 28 },
  { ts: '14:02:13.501', ch: 'shopify', event: 'product.updated', idem: 'new', ms: 22 },
  { ts: '14:02:12.881', ch: 'shopee', event: 'order.created', idem: 'new', ms: 17 },
  { ts: '14:02:12.220', ch: 'lazada', event: 'inventory.synced', idem: 'new', ms: 38 },
];

function WebhookLog() {
  const [ch, setCh] = useState<string>('all');
  const events = WEBHOOKS.filter((e) => ch === 'all' || e.ch === ch);

  const chOptions: [string, string][] = [
    ['all', t('Tất cả kênh', 'All channels')],
    ...CHANNELS.map((c): [string, string] => [c.id, c.short]),
  ];

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <div
        className="hairline-b"
        style={{
          padding: '10px 18px',
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          background: 'var(--bg-soft)',
        }}
      >
        <FilterChip label={t('Kênh', 'Channel')} value={ch} options={chOptions} onChange={setCh} />
        <Pill kind="info">{t('trực tiếp · 50 gần nhất', 'live · latest 50')}</Pill>
        <span style={{ flex: 1 }} />
        <span className="lbl">{t('Định tuyến tenant', 'Tenant routing')}</span>
        <span className="mono" style={{ fontSize: 11 }}>
          {TENANT_DB}
        </span>
        <Pill kind="ok">{t('đã xác minh', 'verified')}</Pill>
      </div>

      <div className="scroll-y" style={{ flex: 1 }}>
        <table className="t-data">
          <thead>
            <tr>
              <th style={{ width: 130 }}>{t('Thời gian', 'Time')}</th>
              <th style={{ width: 100 }}>{t('Kênh', 'Channel')}</th>
              <th style={{ width: 200 }}>{t('Sự kiện', 'Event')}</th>
              <th data-review="idem" style={{ width: 100 }}>
                Idempotency
              </th>
              <th style={{ width: 100, textAlign: 'right' }}>{t('Độ trễ', 'Latency')}</th>
              <th>{t('Định tuyến tới', 'Routed to')}</th>
              <th style={{ width: 50 }} aria-label={t('Chi tiết', 'Detail')} />
            </tr>
          </thead>
          <tbody>
            {events.map((e, i) => (
              <tr key={e.ts + e.ch} className={i === 0 ? 'live-cell' : ''}>
                <td className="mono" style={{ fontSize: 11.5 }}>
                  {e.ts}
                </td>
                <td>
                  <ChannelDot id={e.ch} />
                </td>
                <td>
                  <span className="mono">{e.event}</span>
                </td>
                <td>
                  <Pill kind={e.idem === 'new' ? 'ok' : 'default'}>
                    {e.idem === 'new'
                      ? t('mới · đã lưu', 'new · stored')
                      : t('đã khử trùng', 'deduplicated')}
                  </Pill>
                </td>
                <td
                  className="mono tnum"
                  style={{
                    textAlign: 'right',
                    color: e.ms > 30 ? 'var(--warn-ink)' : 'var(--ink-2)',
                  }}
                >
                  {e.ms}ms
                </td>
                <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-2)' }}>
                  {TENANT_DB} → SG-1
                </td>
                <td>
                  <ChevronRight size={12} style={{ color: 'var(--ink-4)' }} aria-hidden />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Compliance tab ───────────────────────────────────────────────────────────

interface SubProcessor {
  name: string;
  provider: string;
  purpose_vi: string;
  purpose_en: string;
  updated: string;
}

const SUBPROCESSORS: SubProcessor[] = [
  {
    name: 'Postgres (per-tenant)',
    provider: 'Self-managed RDS · ap-southeast-1',
    purpose_vi: 'Kho dữ liệu tenant',
    purpose_en: 'Tenant data store',
    updated: '2026-03-04',
  },
  {
    name: 'Redis',
    provider: 'ElastiCache · ap-southeast-1',
    purpose_vi: 'Cache giữ chỗ · khoá idempotency',
    purpose_en: 'Reservation cache · idempotency keys',
    updated: '2026-02-12',
  },
  {
    name: 'RabbitMQ',
    provider: 'AmazonMQ · ap-southeast-1',
    purpose_vi: 'Nhận webhook · thông điệp saga',
    purpose_en: 'Webhook ingest · saga messages',
    updated: '2026-02-12',
  },
  {
    name: 'Observability (Tempo/Loki)',
    provider: 'Grafana Cloud · SG',
    purpose_vi: 'Traces, logs, metrics',
    purpose_en: 'Traces, logs, metrics',
    updated: '2026-01-30',
  },
];

interface ComplianceAudit {
  ts: string;
  who: string;
  what_vi: string;
  what_en: string;
}

const COMPLIANCE_AUDIT: ComplianceAudit[] = [
  {
    ts: '2026-03-04 09:12',
    who: t('hệ thống', 'system'),
    what_vi: 'Cập nhật phiên bản sub-processor “Tempo” · v2.5.1',
    what_en: 'Sub-processor “Tempo” version updated · v2.5.1',
  },
  {
    ts: '2026-02-28 14:30',
    who: 'khoi.tran@yensaokh.vn',
    what_vi: 'Bỏ che PII · đơn SO-2026-02-2241 · lý do: hoàn tiền',
    what_en: 'PII unmasked · order SO-2026-02-2241 · reason: refund',
  },
  {
    ts: '2026-02-25 11:05',
    who: t('hệ thống', 'system'),
    what_vi: 'Tái tạo công bố PDPA định kỳ',
    what_en: 'Periodic PDPA disclosure regenerated',
  },
  {
    ts: '2026-02-22 16:18',
    who: 'ops.admin@shopflow.vn',
    what_vi: 'Giới hạn phạm vi truy cập tenant · chỉ SG-1',
    what_en: 'Tenant access scope restricted · SG-1 only',
  },
];

function ComplianceTab() {
  return (
    <div
      style={{ padding: 18, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 360px', gap: 14 }}
    >
      <div className="card" data-review="subprocessors">
        <div className="card-h">
          <span className="t">
            {t('Sub-processor · công bố PDPA', 'Sub-processors · PDPA disclosure')}
          </span>
        </div>
        <table className="t-data">
          <thead>
            <tr>
              <th>{t('Dịch vụ', 'Service')}</th>
              <th>{t('Nhà cung cấp / Khu vực', 'Provider / Region')}</th>
              <th>{t('Mục đích', 'Purpose')}</th>
              <th style={{ width: 100 }}>{t('Công bố cập nhật', 'Disclosure updated')}</th>
            </tr>
          </thead>
          <tbody>
            {SUBPROCESSORS.map((s) => (
              <tr key={s.name}>
                <td style={{ fontWeight: 500 }}>{s.name}</td>
                <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-2)' }}>
                  {s.provider}
                </td>
                <td style={{ color: 'var(--ink-2)' }}>{t(s.purpose_vi, s.purpose_en)}</td>
                <td className="mono">{s.updated}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div
          style={{
            padding: '12px 14px',
            borderTop: '1px solid var(--line)',
            background: 'var(--bg-soft)',
            display: 'flex',
            gap: 10,
            alignItems: 'center',
          }}
        >
          <ShieldCheck size={14} strokeWidth={1.5} style={{ color: 'var(--ok)' }} aria-hidden />
          <span style={{ fontSize: 12 }}>
            {t(
              'Dữ liệu tenant được lưu trên Postgres riêng. Không chia sẻ giữa các tenant.',
              'Tenant data lives in a dedicated Postgres database. Never shared across tenants.',
            )}
          </span>
          <span style={{ flex: 1 }} />
          <button className="btn sm" type="button">
            <Download size={11} strokeWidth={1.5} aria-hidden />{' '}
            {t('Xuất công bố', 'Export disclosure')}
          </button>
        </div>
      </div>

      <div className="card">
        <div className="card-h">
          <span className="t">
            {t('Nhật ký audit · 30 ngày gần nhất', 'Audit trail · last 30 days')}
          </span>
        </div>
        <div style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 10 }}>
          {COMPLIANCE_AUDIT.map((a, i) => (
            <div
              key={a.ts}
              style={{
                display: 'grid',
                gridTemplateColumns: '90px 1fr',
                gap: 8,
                fontSize: 11.5,
                paddingBottom: 8,
                borderBottom: i < COMPLIANCE_AUDIT.length - 1 ? '1px solid var(--line)' : 'none',
              }}
            >
              <span className="mono" style={{ color: 'var(--ink-3)' }}>
                {a.ts}
              </span>
              <div>
                <div style={{ color: 'var(--ink)' }}>{t(a.what_vi, a.what_en)}</div>
                <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
                  {t('bởi', 'by')} {a.who}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Connect-channel modal flow ────────────────────────────────────────────────

function ConnectChannelFlow({ onClose }: { onClose: () => void }) {
  const [step, setStep] = useState(1);
  const [picked, setPicked] = useState<ChannelId | null>(null);

  const pickableChannels: Channel[] = [...CHANNELS, SENDO];
  const steps = [
    t('Kênh & OAuth', 'Channel & OAuth'),
    t('Ánh xạ sản phẩm', 'Product mapping'),
    t('Xác nhận', 'Confirm'),
  ];

  return (
    <Fragment>
      <div className="drawer-mask" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('Kết nối một kênh', 'Connect a channel')}
        style={{
          position: 'fixed',
          left: '50%',
          top: '50%',
          transform: 'translate(-50%,-50%)',
          background: 'var(--panel)',
          border: '1px solid var(--line)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-pop)',
          width: 760,
          maxWidth: '92vw',
          maxHeight: '85vh',
          display: 'flex',
          flexDirection: 'column',
          zIndex: 32,
        }}
      >
        <div
          style={{
            padding: '14px 18px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
          }}
        >
          <Plug size={14} strokeWidth={1.5} aria-hidden />
          <span style={{ fontSize: 14, fontWeight: 600 }}>
            {t('Kết nối một kênh', 'Connect a channel')}
          </span>
          <span style={{ flex: 1 }} />
          <span className="mono tnum" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
            {t('Bước', 'Step')} {step} / 3
          </span>
          <button
            className="btn ghost sm"
            type="button"
            onClick={onClose}
            aria-label={t('Đóng', 'Close')}
          >
            <X size={14} aria-hidden />
          </button>
        </div>

        {/* Stepper */}
        <div
          style={{
            padding: '14px 18px',
            display: 'flex',
            alignItems: 'center',
            gap: 14,
            background: 'var(--bg-soft)',
            borderBottom: '1px solid var(--line)',
          }}
        >
          {steps.map((l, i) => {
            const done = step > i + 1;
            const active = step === i + 1;
            return (
              <Fragment key={l}>
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                    opacity: active || done ? 1 : 0.45,
                  }}
                >
                  <div
                    style={{
                      width: 22,
                      height: 22,
                      borderRadius: 11,
                      border: '1px solid var(--line-strong)',
                      background: done ? 'var(--ok)' : active ? 'var(--ink)' : 'var(--panel)',
                      color: step >= i + 1 ? 'var(--ink-inv)' : 'var(--ink-3)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontSize: 11,
                      fontWeight: 600,
                    }}
                  >
                    {done ? <Check size={12} strokeWidth={3} aria-hidden /> : i + 1}
                  </div>
                  <span style={{ fontSize: 12, fontWeight: active ? 600 : 500 }}>{l}</span>
                </div>
                {i < 2 && <div style={{ flex: 1, height: 1, background: 'var(--line)' }} />}
              </Fragment>
            );
          })}
        </div>

        <div className="scroll-y" style={{ flex: 1, padding: 18 }}>
          {step === 1 && (
            <div>
              <div className="lbl" style={{ marginBottom: 8 }}>
                {t('Chọn kênh', 'Choose a channel')}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 8 }}>
                {pickableChannels.map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    onClick={() => setPicked(c.id)}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 10,
                      padding: 12,
                      border: `1px solid ${picked === c.id ? 'var(--accent-line)' : 'var(--line)'}`,
                      background: picked === c.id ? 'var(--accent-soft)' : 'var(--panel)',
                      borderRadius: 4,
                      cursor: 'pointer',
                      textAlign: 'left',
                    }}
                  >
                    <span className="oauth-mark" style={{ background: c.color }}>
                      {c.short.charAt(0)}
                    </span>
                    <div>
                      <div style={{ fontSize: 13, fontWeight: 600 }}>{c.name}</div>
                      <div style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
                        {t('OAuth 2.0 · endpoint SG', 'OAuth 2.0 · SG endpoint')}
                      </div>
                    </div>
                  </button>
                ))}
              </div>

              {picked && (
                <div
                  style={{
                    marginTop: 18,
                    padding: 14,
                    background: 'var(--bg-soft)',
                    border: '1px solid var(--line)',
                    borderRadius: 4,
                  }}
                >
                  <div className="lbl" style={{ marginBottom: 8 }}>
                    {t('OAuth handshake · mô phỏng', 'OAuth handshake · simulated')}
                  </div>
                  <div
                    className="mono"
                    style={{
                      fontSize: 11,
                      color: 'var(--ink-2)',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 4,
                    }}
                  >
                    <div>
                      → GET
                      /oauth/authorize?client_id=shopflow_prod&redirect_uri=https://app.shopflow.vn/cb
                    </div>
                    <div>← 200 · code=ZX-94f3a2b1d…</div>
                    <div>→ POST /oauth/token · exchange · expires 7200s</div>
                    <div style={{ color: 'var(--ok)' }}>
                      ✓ access_token stored · scope=read_orders write_inventory read_products
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {step === 2 && <ProductMapping />}

          {step === 3 && (
            <div>
              <div className="lbl">{t('Sẵn sàng kết nối', 'Ready to connect')}</div>
              <div
                style={{
                  marginTop: 8,
                  padding: 18,
                  background: 'var(--accent-soft)',
                  border: '1px solid var(--accent-line)',
                  borderRadius: 4,
                }}
              >
                <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--accent-ink)' }}>
                  {picked ? chBy(picked).name : ''} · {t('sẵn sàng', 'ready')}
                </div>
                <div style={{ fontSize: 12, color: 'var(--ink-2)', marginTop: 6 }}>
                  {t(
                    '11 sản phẩm đã ánh xạ (8 chính xác · 2 gần đúng đã nhận · 1 thủ công). Đã đăng ký webhook endpoint. Đồng bộ ban đầu sẽ khởi động khi xác nhận.',
                    '11 products mapped (8 exact · 2 fuzzy accepted · 1 manual). Webhook endpoint registered. Initial sync starts on confirm.',
                  )}
                </div>
              </div>
            </div>
          )}
        </div>

        <div
          style={{
            padding: 12,
            borderTop: '1px solid var(--line)',
            background: 'var(--bg-soft)',
            display: 'flex',
            gap: 8,
          }}
        >
          {step > 1 && (
            <button className="btn" type="button" onClick={() => setStep(step - 1)}>
              <ChevronLeft size={12} aria-hidden /> {t('Quay lại', 'Back')}
            </button>
          )}
          <span style={{ flex: 1 }} />
          <button className="btn" type="button" onClick={onClose}>
            {t('Huỷ', 'Cancel')}
          </button>
          {step < 3 && (
            <button
              className="btn primary"
              type="button"
              disabled={!picked}
              onClick={() => setStep(step + 1)}
            >
              {t('Tiếp tục', 'Continue')} <ChevronRight size={12} aria-hidden />
            </button>
          )}
          {step === 3 && (
            <button className="btn accent" type="button" onClick={onClose}>
              <Check size={12} aria-hidden /> {t('Kết nối', 'Connect')}
            </button>
          )}
        </div>
      </div>
    </Fragment>
  );
}

type MappingKind = 'exact' | 'fuzzy' | 'manual';

interface MappingRow {
  local: string;
  name: string;
  remote: string | null;
  conf: number;
  kind: MappingKind;
}

const MAPPING_ROWS: MappingRow[] = [
  {
    local: 'YS-TINH-CHE-100G',
    name: 'Yến sào tinh chế 100g',
    remote: 'Yến tinh chế 100g – Khánh Hòa',
    conf: 0.98,
    kind: 'exact',
  },
  {
    local: 'YS-NGUYEN-TO-50G',
    name: 'Yến sào nguyên tổ 50g',
    remote: 'Yến sào nguyên tổ 50g',
    conf: 1.0,
    kind: 'exact',
  },
  {
    local: 'YS-CHUNG-DUONG-6',
    name: 'Yến chưng đường phèn 6 hũ',
    remote: 'Yến chưng đường phèn (combo 6)',
    conf: 0.92,
    kind: 'exact',
  },
  {
    local: 'AT-COT-NAM-L-001',
    name: 'Áo thun cotton nam size L',
    remote: 'Áo thun nam cotton 100% – Size L',
    conf: 0.78,
    kind: 'fuzzy',
  },
  {
    local: 'AT-COT-NAM-M-001',
    name: 'Áo thun cotton nam size M',
    remote: 'Áo thun nam cotton 100% – Size M',
    conf: 0.78,
    kind: 'fuzzy',
  },
  {
    local: 'KB-MIENG-LON-200G',
    name: 'Khô bò miếng lớn 200g',
    remote: 'Khô bò 200g - Miếng lớn',
    conf: 0.85,
    kind: 'fuzzy',
  },
  {
    local: 'CF-RANG-XAY-500G',
    name: 'Cà phê rang xay 500g · Robusta',
    remote: 'Cà phê Robusta xay 500g',
    conf: 0.81,
    kind: 'exact',
  },
  {
    local: 'CF-HAT-NGUYEN-1KG',
    name: 'Cà phê hạt nguyên 1kg · Arabica',
    remote: null,
    conf: 0,
    kind: 'manual',
  },
  {
    local: 'LV-KHAN-LUA-001',
    name: 'Khăn lụa tơ tằm Vạn Phúc',
    remote: 'Khăn lụa Vạn Phúc (silk)',
    conf: 0.74,
    kind: 'fuzzy',
  },
  {
    local: 'TY-RANG-DEN-250ML',
    name: 'Tinh dầu tràm 250ml · Huế',
    remote: 'Tinh dầu tràm Huế 250ml',
    conf: 0.94,
    kind: 'exact',
  },
  {
    local: 'NH-MAT-ONG-500G',
    name: 'Mật ong rừng nguyên chất 500g',
    remote: 'Mật ong rừng 500g nguyên chất',
    conf: 0.88,
    kind: 'exact',
  },
];

function ProductMapping() {
  return (
    <div data-review="product-mapping">
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 12 }}>
        <div>
          <div className="lbl">{t('Ánh xạ sản phẩm', 'Product mapping')}</div>
          <div style={{ fontSize: 12, color: 'var(--ink-2)' }}>
            {t(
              'Nhận diện SKU theo mã SKU · gần đúng theo tên · mã vạch',
              'Match by SKU code · fuzzy by name · barcode',
            )}
          </div>
        </div>
        <span style={{ flex: 1 }} />
        <div style={{ display: 'flex', gap: 12, fontSize: 11.5 }}>
          <span>
            <span className="s-dot ok" style={{ marginRight: 4 }} />
            {t('Chính xác 8', 'Exact 8')}
          </span>
          <span>
            <span className="s-dot warn" style={{ marginRight: 4 }} />
            {t('Gần đúng 4', 'Fuzzy 4')}
          </span>
          <span>
            <span className="s-dot bad" style={{ marginRight: 4 }} />
            {t('Thủ công 1', 'Manual 1')}
          </span>
        </div>
      </div>

      <div style={{ border: '1px solid var(--line)', borderRadius: 4, overflow: 'hidden' }}>
        <table className="t-data" style={{ borderRadius: 4 }}>
          <thead>
            <tr>
              <th>{t('SKU của bạn', 'Your SKU')}</th>
              <th>{t('Sản phẩm trên kênh', 'Channel product')}</th>
              <th style={{ width: 110 }}>{t('Độ tin cậy', 'Confidence')}</th>
              <th style={{ width: 160 }}>{t('Quyết định', 'Decision')}</th>
            </tr>
          </thead>
          <tbody>
            {MAPPING_ROWS.map((r) => {
              const confKind = r.conf > 0.9 ? 'ok' : r.conf > 0.7 ? 'warn' : 'bad';
              return (
                <tr key={r.local}>
                  <td>
                    <div style={{ fontSize: 12.5 }}>{r.name}</div>
                    <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
                      {r.local}
                    </div>
                  </td>
                  <td>
                    {r.remote ? (
                      <span style={{ fontSize: 12.5, color: 'var(--ink-2)' }}>{r.remote}</span>
                    ) : (
                      <span style={{ fontSize: 12.5, color: 'var(--ink-4)', fontStyle: 'italic' }}>
                        {t('— chưa tìm thấy đối sánh —', '— no match found —')}
                      </span>
                    )}
                  </td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <div className={'bar ' + confKind} style={{ width: 60 }}>
                        <i style={{ width: Math.round(r.conf * 100) + '%' }} />
                      </div>
                      <span className="mono tnum" style={{ fontSize: 11 }}>
                        {r.conf ? `${(r.conf * 100).toFixed(0)}%` : '—'}
                      </span>
                    </div>
                  </td>
                  <td>
                    {r.kind === 'exact' && <Pill kind="ok">{t('chấp nhận', 'accepted')}</Pill>}
                    {r.kind === 'fuzzy' && (
                      <span style={{ display: 'flex', gap: 4 }}>
                        <button className="btn sm" type="button">
                          {t('Chấp nhận', 'Accept')}
                        </button>
                        <button className="btn sm ghost" type="button">
                          {t('Thay đổi', 'Change')}
                        </button>
                      </span>
                    )}
                    {r.kind === 'manual' && (
                      <button className="btn sm accent" type="button">
                        {t('Ánh xạ thủ công', 'Map manually')}
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Shared bits ──────────────────────────────────────────────────────────────

function ChannelDot({ id, label = true }: { id: string; label?: boolean }) {
  const ch = chBy(id);
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span className="ch-dot" style={{ background: ch.color, width: 8, height: 8 }} />
      {label && <span style={{ fontSize: 12, color: 'var(--ink-2)' }}>{ch.short}</span>}
    </span>
  );
}

function FilterChip({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: [string, string][];
  onChange: (v: string) => void;
}) {
  return (
    <div
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '0 8px 0 10px',
        border: '1px solid var(--line)',
        borderRadius: 3,
        height: 28,
        background: 'var(--panel)',
      }}
    >
      <span className="lbl">{label}</span>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={{ border: 'none', background: 'transparent', height: 26, fontSize: 12 }}
        aria-label={label}
      >
        {options.map(([v, l]) => (
          <option key={v} value={v}>
            {l}
          </option>
        ))}
      </select>
    </div>
  );
}
