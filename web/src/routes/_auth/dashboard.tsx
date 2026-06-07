import { Fragment, useEffect, useMemo, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { ChevronRight, ArrowUpRight, AlertTriangle, Check } from 'lucide-react';
import { Pill } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Dashboard — the Operations-Manager command surface (design-handoff screen).
 *
 * Ported from the design handoff `screen-dashboard.jsx`. The handoff carried
 * three role variants (operator / manager / seller) behind a role switcher;
 * the real source has no role switcher, so this renders the primary
 * Ops-Manager / Owner view only and drops the operator + seller branches.
 *
 * Layout: command header (a live-aging SLA-breach focal panel + a vitals
 * cluster — reserve p99 Δ-vs-fleet, fulfillment p50, channel health, live
 * connections) → order pipeline funnel (four in-flight saga stages
 * with flow chevrons + share-of-WIP bars + a breach focal stage, then a
 * split-off terminal "Shipped today" total) → manager body grid
 * (active-SLA-breach table on the left; picker
 * performance + fulfillment-time sparkline + breach-cause bars on the right)
 * → full-width channel-health strip (per-channel circuit-breaker state +
 * rate-limit headroom).
 *
 * Data is mocked in the frontend (no dashboard/KPI backend endpoints exist
 * yet — wire to real APIs + SignalR later). The breach table ages every
 * second via a 1s ticker, mirroring the handoff's live SLA clock.
 * `data-review="border-card"` + `data-tour="..."` anchors preserved from the
 * handoff (QA + guided-tour contract).
 */

// ── Mock channels ────────────────────────────────────────────────────────---

interface ChannelMeta {
  id: string;
  short: string;
  color: string;
}

const CHANNELS: Record<string, ChannelMeta> = {
  shopee: { id: 'shopee', short: 'Shopee', color: 'var(--ch-shopee)' },
  lazada: { id: 'lazada', short: 'Lazada', color: 'var(--ch-lazada)' },
  tiktok: { id: 'tiktok', short: 'TikTok', color: 'var(--ch-tiktok)' },
  shopify: { id: 'shopify', short: 'Shopify', color: 'var(--ch-shopify)' },
};

function chBy(id: string): ChannelMeta {
  return CHANNELS[id] ?? { id, short: id, color: 'var(--ink-3)' };
}

const HEALTH = {
  tenantP99Ms: 142,
  fleetMedianMs: 218,
  signalrConns: 12,
};

// ── Pipeline ────────────────────────────────────────────────────────────────

interface PipelineStage {
  stage: 'New' | 'Reserved' | 'Picking' | 'Packed' | 'Shipped';
  count: number;
  breach: number;
}

const PIPELINE: PipelineStage[] = [
  { stage: 'New', count: 142, breach: 0 },
  { stage: 'Reserved', count: 88, breach: 0 },
  { stage: 'Picking', count: 41, breach: 7 },
  { stage: 'Packed', count: 27, breach: 0 },
  { stage: 'Shipped', count: 1248, breach: 0 },
];

const STAGE_LABEL: Record<PipelineStage['stage'], string> = {
  New: 'Mới',
  Reserved: 'Đã giữ',
  Picking: 'Đang nhặt',
  Packed: 'Đã đóng',
  Shipped: 'Đã giao',
};

const STAGE_SUB: Record<PipelineStage['stage'], { vi: string; en: string }> = {
  New: { vi: 'webhook → giữ chỗ', en: 'webhook → reserve' },
  Reserved: { vi: 'tồn đã giữ · có idem key', en: 'stock held · idem key' },
  Picking: { vi: 'đã phân đợt nhặt', en: 'pick wave assigned' },
  Packed: { vi: 'chờ nhãn vận chuyển', en: 'awaiting ship label' },
  Shipped: { vi: 'tổng hôm nay', en: 'total today' },
};

// ── SLA breaches ──────────────────────────────────────────────────────────--

interface Breach {
  id: string;
  ch: string;
  ageSec: number;
  reason_vi: string;
  reason_en: string;
  picker: string;
}

const BREACHES: Breach[] = [
  {
    id: 'SO-2026-05-0042',
    ch: 'shopee',
    ageSec: 412,
    reason_vi: 'Nhân viên ngưng 6m · khu B-04 chưa phân công',
    reason_en: 'Picker idle 6m · zone B-04 unassigned',
    picker: '—',
  },
  {
    id: 'SO-2026-05-0039',
    ch: 'tiktok',
    ageSec: 287,
    reason_vi: 'SKU AT-COT-NAM-L-001 thiếu 2 · nguy cơ bán vượt',
    reason_en: 'SKU AT-COT-NAM-L-001 short by 2 · oversell risk',
    picker: 'Nguyễn V.A.',
  },
  {
    id: 'SO-2026-05-0038',
    ch: 'lazada',
    ageSec: 244,
    reason_vi: 'API nhãn vận chuyển hết giờ · GHN',
    reason_en: 'Carrier label API timeout · GHN',
    picker: 'Phạm T.H.',
  },
  {
    id: 'SO-2026-05-0036',
    ch: 'shopee',
    ageSec: 198,
    reason_vi: 'SKU lạnh · hàng chờ đóng đông',
    reason_en: 'Cold-chain SKU · packing freezer queue',
    picker: 'Lê V.D.',
  },
  {
    id: 'SO-2026-05-0033',
    ch: 'tiktok',
    ageSec: 156,
    reason_vi: 'Nhân viên ngưng 3m · khu A-12',
    reason_en: 'Picker idle 3m · zone A-12',
    picker: '—',
  },
  {
    id: 'SO-2026-05-0029',
    ch: 'shopify',
    ageSec: 121,
    reason_vi: 'Xác thực địa chỉ thất bại · cần kiểm tra lại',
    reason_en: 'Address validation failed · re-check needed',
    picker: 'Hoàng M.T.',
  },
  {
    id: 'SO-2026-05-0024',
    ch: 'lazada',
    ageSec: 73,
    reason_vi: 'Đợt 04 chưa xong · 2/3 SKU đã quét',
    reason_en: 'Wave 04 incomplete · 2/3 SKUs scanned',
    picker: 'Phạm T.H.',
  },
];

// ── Pickers / sparkline / breach causes ─────────────────────────────────────

interface Picker {
  name: string;
  throughput: number;
  idle: string;
  ok: number;
}

const PICKERS: Picker[] = [
  { name: 'Phạm Thị Hà', throughput: 38, idle: '0m', ok: 0.98 },
  { name: 'Nguyễn Văn An', throughput: 36, idle: '2m', ok: 0.97 },
  { name: 'Lê Văn Đức', throughput: 31, idle: '0m', ok: 0.99 },
  { name: 'Hoàng Minh Tuấn', throughput: 28, idle: '4m', ok: 0.95 },
  { name: 'Đỗ Quỳnh Anh', throughput: 24, idle: '0m', ok: 0.96 },
];

const FULFIL_SPARK: number[] = [
  12, 11, 13, 14, 12, 11, 10, 9, 11, 12, 18, 22, 21, 19, 16, 14, 13, 12, 15, 17, 19, 28, 24, 18,
];

interface BreachCause {
  vi: string;
  en: string;
  count: number;
  pct: number;
}

const BREACH_CAUSES: BreachCause[] = [
  { vi: 'Nhân viên ngưng / chưa phân công', en: 'Picker idle / unassigned', count: 14, pct: 0.41 },
  { vi: 'API nhãn vận chuyển hết giờ', en: 'Carrier label API timeout', count: 8, pct: 0.24 },
  { vi: 'Hàng lạnh chờ đóng đông', en: 'Cold-chain queue back-up', count: 5, pct: 0.15 },
  { vi: 'Xác thực địa chỉ thất bại', en: 'Address validation fail', count: 4, pct: 0.12 },
  { vi: 'Khác', en: 'Other', count: 3, pct: 0.08 },
];

// ── Channel health strip ─────────────────────────────────────────────────---

interface ChannelHealth {
  id: string;
  ordersToday: number;
  rate: number;
  breaker: 'closed' | 'half-open' | 'open';
  lastSync: string;
}

const CHANNEL_STRIP: ChannelHealth[] = [
  { id: 'shopee', ordersToday: 542, rate: 0.86, breaker: 'closed', lastSync: '4s' },
  { id: 'lazada', ordersToday: 388, rate: 0.71, breaker: 'closed', lastSync: '7s' },
  { id: 'tiktok', ordersToday: 691, rate: 0.92, breaker: 'closed', lastSync: '2s' },
  { id: 'shopify', ordersToday: 102, rate: 0.94, breaker: 'half-open', lastSync: '38s' },
];

// ── Helpers ──────────────────────────────────────────────────────────────---

function fmtAge(s: number): string {
  if (s < 5) return t('vừa xong', 'just now');
  if (s < 60) return t(`${s} giây trước`, `${s}s ago`);
  const m = Math.floor(s / 60);
  if (m < 60) return t(`${m} phút trước`, `${m}m ago`);
  const h = Math.floor(m / 60);
  if (h < 24) return t(`${h} giờ trước`, `${h}h ago`);
  const d = new Date(Date.now() - s * 1000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getHours())}:${pad(d.getMinutes())} · ${pad(d.getDate())}/${pad(d.getMonth() + 1)}`;
}

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/dashboard')({
  component: DashboardRouteComponent,
});

function DashboardRouteComponent() {
  useLocale();
  // 1s ticker so the SLA breach ages tick up live (mirrors the handoff clock).
  const [, setTick] = useState(0);
  const ageBase = useMemo(() => Date.now(), []);
  const [ageAdj, setAgeAdj] = useState(0);
  useEffect(() => {
    const id = setInterval(() => {
      setTick((n) => n + 1);
      setAgeAdj(Math.floor((Date.now() - ageBase) / 1000));
    }, 1000);
    return () => clearInterval(id);
  }, [ageBase]);

  return (
    <div className="scroll-y" style={{ flex: 1, minHeight: 0 }}>
      <CommandHeader ageAdj={ageAdj} />
      <PipelineCard />
      <ManagerBody ageAdj={ageAdj} />
    </div>
  );
}

// ── Command header ─────────────────────────────────────────────────────────-

/**
 * The command header leads with the single thing an ops manager must act on:
 * how many orders are breaching SLA right now, the oldest one aging live, and
 * the one-click way to clear it. When nothing is breaching it flips to a calm
 * all-clear. To its right, a vitals cluster (reserve p99 vs fleet, fulfillment
 * p50, channel health, live connections) reads at a glance. This replaces the
 * thin status strip that buried these signals in tiny right-aligned text.
 */
function CommandHeader({ ageAdj }: { ageAdj: number }) {
  const breaches = BREACHES.length;
  const oldest = BREACHES.reduce((a, b) => (b.ageSec > a.ageSec ? b : a), BREACHES[0]!);
  const unassigned = BREACHES.filter((b) => b.picker === '—').length;
  const breachActive = breaches > 0;

  const delta = HEALTH.fleetMedianMs - HEALTH.tenantP99Ms;
  const faster = delta >= 0;
  const channelsOk = CHANNEL_STRIP.filter((c) => c.breaker === 'closed').length;
  const channelsTotal = CHANNEL_STRIP.length;
  const channelsHealthy = channelsOk === channelsTotal;

  return (
    <div
      data-tour="fairness"
      className="hairline-b"
      style={{ display: 'flex', alignItems: 'stretch', background: 'var(--panel)' }}
    >
      {/* Focal: what needs attention now */}
      <div
        style={{
          flex: '0 0 auto',
          minWidth: 300,
          padding: '14px 20px',
          background: breachActive ? 'var(--bad-soft)' : 'var(--ok-soft)',
          borderRight: '1px solid var(--line)',
          display: 'flex',
          flexDirection: 'column',
          gap: 5,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span className={`live-dot ${breachActive ? 'bad' : 'ok'}`} />
          <span
            className="lbl"
            style={{ color: breachActive ? 'var(--bad-ink)' : 'var(--ok-ink)' }}
          >
            {t('Cần xử lý ngay', 'Needs attention')}
          </span>
        </div>

        {breachActive ? (
          <>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
              <span
                className="mono tnum"
                style={{
                  fontSize: 38,
                  fontWeight: 700,
                  lineHeight: 1,
                  color: 'var(--bad-ink)',
                  letterSpacing: '-0.02em',
                }}
              >
                {breaches}
              </span>
              <span style={{ fontSize: 13, color: 'var(--bad-ink)', fontWeight: 500 }}>
                {t('đơn vi phạm SLA', 'orders breaching SLA')}
              </span>
            </div>
            <div className="mono" style={{ fontSize: 11.5, color: 'var(--bad-ink)' }}>
              {t('cũ nhất', 'oldest')} {fmtAge(oldest.ageSec + ageAdj)} · {chBy(oldest.ch).short}
              {unassigned > 0 ? ` · ${unassigned} ${t('chưa phân công', 'unassigned')}` : ''}
            </div>
            <button
              className="btn sm danger"
              type="button"
              style={{ alignSelf: 'flex-start', marginTop: 3 }}
            >
              {t('Phân công tất cả', 'Assign all')}{' '}
              <ChevronRight size={11} strokeWidth={1.5} aria-hidden />
            </button>
          </>
        ) : (
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, paddingTop: 4 }}>
            <Check size={26} strokeWidth={2.5} style={{ color: 'var(--ok)' }} aria-hidden />
            <span style={{ fontSize: 15, color: 'var(--ok-ink)', fontWeight: 600 }}>
              {t('Không có vi phạm SLA', 'No SLA breaches')}
            </span>
          </div>
        )}
      </div>

      {/* Vitals cluster */}
      <div style={{ flex: 1, display: 'flex', alignItems: 'stretch' }}>
        <Vital
          first
          label={t('p99 giữ chỗ · tenant', 'reserve p99 · tenant')}
          value={`${HEALTH.tenantP99Ms}ms`}
          delta={`${faster ? '↓' : '↑'} ${Math.abs(delta)}ms ${t('so với fleet', 'vs fleet')} ${HEALTH.fleetMedianMs}`}
          deltaKind={faster ? 'ok' : 'bad'}
        />
        <Vital
          label={t('Thời gian xử lý · p50', 'fulfillment · p50')}
          value="18m"
          delta={t('↑ 22% so với hôm qua', '↑ 22% vs yesterday')}
          deltaKind="warn"
        />
        <Vital
          label={t('Sức khoẻ kênh', 'channel health')}
          value={`${channelsOk}/${channelsTotal}`}
          delta={
            channelsHealthy
              ? t('tất cả ổn định', 'all stable')
              : t('1 giảm hiệu suất', '1 degraded')
          }
          deltaKind={channelsHealthy ? 'ok' : 'warn'}
        />
        <Vital
          label={t('Trực tiếp', 'live')}
          value={`${HEALTH.signalrConns}`}
          delta={t('kết nối signalr', 'signalr conns')}
          deltaKind="muted"
        />
      </div>
    </div>
  );
}

type VitalKind = 'ok' | 'warn' | 'bad' | 'muted';
const VITAL_DELTA_COLOR: Record<VitalKind, string> = {
  ok: 'var(--ok)',
  warn: 'var(--warn-ink)',
  bad: 'var(--bad-ink)',
  muted: 'var(--ink-3)',
};

function Vital({
  label,
  value,
  delta,
  deltaKind,
  first = false,
}: {
  label: string;
  value: string;
  delta: string;
  deltaKind: VitalKind;
  first?: boolean;
}) {
  return (
    <div
      style={{
        flex: '1 1 0',
        minWidth: 0,
        padding: '14px 16px',
        borderLeft: first ? 'none' : '1px solid var(--line)',
        display: 'flex',
        flexDirection: 'column',
        gap: 3,
        justifyContent: 'center',
      }}
    >
      <span className="lbl" style={{ color: 'var(--ink-3)' }}>
        {label}
      </span>
      <span
        className="mono tnum"
        style={{ fontSize: 22, fontWeight: 600, lineHeight: 1.1, letterSpacing: '-0.01em' }}
      >
        {value}
      </span>
      <span
        className="mono tnum"
        style={{
          fontSize: 11,
          fontWeight: 600,
          color: VITAL_DELTA_COLOR[deltaKind],
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {delta}
      </span>
    </div>
  );
}

// ── Pipeline card ────────────────────────────────────────────────────────---

const WIP_STAGES = PIPELINE.filter((p) => p.stage !== 'Shipped');
const SHIPPED_STAGE = PIPELINE.find((p) => p.stage === 'Shipped')!;
const WIP_TOTAL = WIP_STAGES.reduce((n, p) => n + p.count, 0);

/**
 * The pipeline reads as a flow, not five equal stat cells. Chevrons carry the
 * left-to-right direction through the four in-flight stages; a share-of-WIP
 * micro-bar under each shows where volume sits (the funnel narrowing); the
 * breached stage is the focal alert. "Shipped" is split off past a heavier
 * divider as the calm terminal total so the day's cumulative count stops
 * out-shouting the work that still needs attention.
 */
function PipelineCard() {
  return (
    <div style={{ padding: '16px 18px 4px' }}>
      <div className="lbl" style={{ marginBottom: 8 }}>
        {t('Quy trình đơn hàng · 60 phút gần nhất', 'Order pipeline · last 60 minutes')}
      </div>
      <div
        className="card"
        data-review="border-card"
        style={{ overflow: 'hidden', display: 'flex', alignItems: 'stretch' }}
      >
        {WIP_STAGES.map((p, i) => (
          <Fragment key={p.stage}>
            <PipelineStageCell p={p} />
            {i < WIP_STAGES.length - 1 && (
              <span style={{ display: 'flex', alignItems: 'center', padding: '0 2px' }} aria-hidden>
                <ChevronRight size={16} strokeWidth={1.5} style={{ color: 'var(--ink-4)' }} />
              </span>
            )}
          </Fragment>
        ))}
        <span style={{ width: 1, background: 'var(--line-strong)', margin: '10px 0' }} />
        <ShippedCell />
      </div>
    </div>
  );
}

function PipelineStageCell({ p }: { p: PipelineStage }) {
  const breached = p.breach > 0;
  const sub = STAGE_SUB[p.stage];
  const share = Math.round((p.count / WIP_TOTAL) * 100);
  return (
    <div
      style={{
        flex: 1,
        padding: '15px 16px 13px',
        background: breached ? 'var(--bad-soft)' : 'transparent',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          minHeight: 18,
        }}
      >
        <span className="lbl" style={{ color: breached ? 'var(--bad-ink)' : 'var(--ink-3)' }}>
          {t(STAGE_LABEL[p.stage], p.stage)}
        </span>
        {breached && (
          <Pill kind="bad">
            <AlertTriangle size={9} strokeWidth={2.5} aria-hidden style={{ marginRight: 2 }} />
            {p.breach} {t('vi phạm', 'breached')}
          </Pill>
        )}
      </div>
      <div
        className="mono tnum"
        style={{
          fontSize: 30,
          fontWeight: 600,
          color: breached ? 'var(--bad-ink)' : 'var(--ink)',
          letterSpacing: '-0.02em',
          lineHeight: 1.1,
          marginTop: 2,
        }}
      >
        {p.count.toLocaleString()}
      </div>
      <div style={{ fontSize: 11, color: 'var(--ink-3)', marginTop: 3 }}>{t(sub.vi, sub.en)}</div>
      <div className="bar" style={{ marginTop: 9 }}>
        <i
          style={{ width: share + '%', background: breached ? 'var(--bad)' : 'var(--neutral-400)' }}
        />
      </div>
    </div>
  );
}

function ShippedCell() {
  return (
    <div style={{ flex: 1, padding: '15px 16px 13px', background: 'var(--bg-soft)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 5, minHeight: 18 }}>
        <Check size={11} strokeWidth={3} style={{ color: 'var(--ok)' }} aria-hidden />
        <span className="lbl" style={{ color: 'var(--ink-3)' }}>
          {t('Đã giao', 'Shipped')}
        </span>
      </div>
      <div
        className="mono tnum"
        style={{
          fontSize: 30,
          fontWeight: 600,
          color: 'var(--ink-2)',
          letterSpacing: '-0.02em',
          lineHeight: 1.1,
          marginTop: 2,
        }}
      >
        {SHIPPED_STAGE.count.toLocaleString()}
      </div>
      <div style={{ fontSize: 11, color: 'var(--ink-3)', marginTop: 3 }}>
        {t('hoàn tất hôm nay', 'completed today')}
      </div>
    </div>
  );
}

// ── Manager body ─────────────────────────────────────────────────────────---

function ManagerBody({ ageAdj }: { ageAdj: number }) {
  return (
    <div
      style={{
        padding: 18,
        display: 'grid',
        gridTemplateColumns: 'minmax(0, 1fr) 360px',
        gap: 14,
      }}
    >
      <BreachTable ageAdj={ageAdj} />
      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        <PickerPerformance />
        <FulfillmentCard />
        <BreachCauses />
      </div>
      <ChannelStrip />
    </div>
  );
}

function BreachTable({ ageAdj }: { ageAdj: number }) {
  return (
    <div
      data-tour="sla-breach"
      data-review="border-card"
      className="card"
      style={{ overflow: 'hidden' }}
    >
      <div
        className="card-h"
        style={{ background: 'var(--bad-soft)', borderBottom: '1px solid var(--bad-line)' }}
      >
        <span className="live-dot bad" />
        <span className="t" style={{ color: 'var(--bad-ink)' }}>
          {t('Vi phạm SLA · đang hoạt động', 'SLA breaches · active')}
        </span>
        <span style={{ color: 'var(--bad-ink)', fontSize: 11.5 }}>
          · {t(`${BREACHES.length} đơn quá hạn`, `${BREACHES.length} orders past SLA`)}
        </span>
        <span style={{ flex: 1 }} />
        <button className="btn sm" type="button">
          {t('Phân công tất cả', 'Assign all')}{' '}
          <ChevronRight size={11} strokeWidth={1.5} aria-hidden />
        </button>
      </div>
      <table className="t-data">
        <thead>
          <tr>
            <th style={{ width: 150 }}>{t('Đơn', 'Order')}</th>
            <th style={{ width: 80 }}>{t('Kênh', 'Channel')}</th>
            <th style={{ width: 80 }}>{t('Tuổi', 'Age')}</th>
            <th>{t('Lý do vi phạm', 'Breach reason')}</th>
            <th style={{ width: 130 }}>{t('Nhân viên nhặt', 'Picker')}</th>
            <th style={{ width: 60 }}>
              <span className="sr-only">{t('Mở đơn', 'Open order')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {BREACHES.map((b) => {
            const unassigned = b.picker === '—';
            return (
              <tr key={b.id} style={{ cursor: 'pointer' }}>
                <td>
                  <span className="mono" style={{ fontWeight: 600 }}>
                    {b.id}
                  </span>
                </td>
                <td>
                  <ChannelDot id={b.ch} />
                </td>
                <td
                  className="breach-cell"
                  style={{
                    fontFamily: 'var(--font-mono)',
                    color: 'var(--bad-ink)',
                    fontWeight: 600,
                  }}
                >
                  {fmtAge(b.ageSec + ageAdj)}
                </td>
                <td style={{ color: 'var(--ink-2)' }}>{t(b.reason_vi, b.reason_en)}</td>
                <td style={{ color: unassigned ? 'var(--ink-4)' : 'var(--ink-2)' }}>{b.picker}</td>
                <td>
                  <ArrowUpRight
                    size={12}
                    strokeWidth={1.5}
                    style={{ color: 'var(--ink-3)' }}
                    aria-hidden
                  />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function PickerPerformance() {
  return (
    <div className="card">
      <div className="card-h">
        <span className="t">
          {t('Hiệu suất nhặt hàng · hôm nay', 'Picker performance · today')}
        </span>
      </div>
      <div style={{ padding: '8px 0' }}>
        {PICKERS.map((p, i) => (
          <div
            key={p.name}
            style={{
              display: 'grid',
              gridTemplateColumns: '18px 1fr auto auto',
              alignItems: 'center',
              gap: 10,
              padding: '6px 14px',
            }}
          >
            <span className="mono" style={{ fontSize: 10.5, color: 'var(--ink-4)' }}>
              {String(i + 1).padStart(2, '0')}
            </span>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontSize: 12.5, fontWeight: 500 }}>{p.name}</div>
              <div style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
                {t(`ngưng ${p.idle}`, `idle ${p.idle}`)} ·{' '}
                {t(`chính xác ${(p.ok * 100).toFixed(0)}%`, `${(p.ok * 100).toFixed(0)}% accuracy`)}
              </div>
            </div>
            <div style={{ width: 80 }}>
              <Bar value={p.throughput} max={40} kind="accent" width={80} />
            </div>
            <span className="mono tnum" style={{ fontSize: 12, width: 32, textAlign: 'right' }}>
              {p.throughput}/h
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

function FulfillmentCard() {
  return (
    <div className="card">
      <div className="card-h">
        <span className="t">
          {t('Thời gian xử lý · 6h gần nhất', 'Fulfillment time · last 6h')}
        </span>
        <span style={{ flex: 1 }} />
        <span className="mono tnum" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
          {t('khoảng 30 phút', '~30 min buckets')}
        </span>
      </div>
      <div style={{ padding: '14px 16px', display: 'flex', alignItems: 'center', gap: 14 }}>
        <Sparkline data={FULFIL_SPARK} width={180} height={36} color="var(--accent)" />
        <div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 4 }}>
            <span className="mono tnum" style={{ fontSize: 22, fontWeight: 600 }}>
              18
            </span>
            <span style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              {t('phút · p50', 'min · p50')}
            </span>
          </div>
          <div style={{ fontSize: 11, color: 'var(--warn-ink)' }}>
            {t('↑ 22% so với hôm qua', '↑ 22% vs yesterday')}
          </div>
        </div>
      </div>
    </div>
  );
}

function BreachCauses() {
  return (
    <div className="card">
      <div className="card-h">
        <span className="t">{t('Nguyên nhân vi phạm · 24h', 'Breach causes · 24h')}</span>
      </div>
      <div style={{ padding: '10px 14px 14px' }}>
        {BREACH_CAUSES.map((c) => (
          <div key={c.en} style={{ marginBottom: 8 }}>
            <div
              style={{
                display: 'flex',
                alignItems: 'baseline',
                justifyContent: 'space-between',
              }}
            >
              <span style={{ fontSize: 12 }}>{t(c.vi, c.en)}</span>
              <span className="mono tnum" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
                {c.count} · {(c.pct * 100).toFixed(0)}%
              </span>
            </div>
            <Bar value={c.pct * 100} kind="warn" />
          </div>
        ))}
      </div>
    </div>
  );
}

function ChannelStrip() {
  return (
    <div
      data-tour="channel-strip"
      className="card"
      style={{ gridColumn: '1 / -1', overflow: 'hidden' }}
    >
      <div className="card-h">
        <span className="t">{t('Sức khoẻ kênh bán', 'Channel health')}</span>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)' }}>
        {CHANNEL_STRIP.map((c, i) => (
          <div
            key={c.id}
            style={{
              padding: '12px 16px',
              borderRight: i < 3 ? '1px solid var(--line)' : 'none',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <ChannelDot id={c.id} />
              <span style={{ flex: 1 }} />
              <Pill kind={c.breaker === 'closed' ? 'ok' : 'warn'}>cb · {c.breaker}</Pill>
            </div>
            <div className="mono tnum" style={{ fontSize: 22, fontWeight: 600, marginTop: 6 }}>
              {c.ordersToday}
            </div>
            <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              {t(`đơn · đồng bộ gần nhất ${c.lastSync}`, `orders · last sync ${c.lastSync}`)}
            </div>
            <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 6 }}>
              <span className="lbl" style={{ width: 90 }}>
                rate-limit
              </span>
              <Bar
                value={c.rate * 100}
                kind={c.rate < 0.5 ? 'bad' : c.rate < 0.75 ? 'warn' : 'ok'}
              />
              <span className="mono tnum" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
                {(c.rate * 100).toFixed(0)}%
              </span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Shared primitives (inlined — source has no equivalents) ─────────────────-

function ChannelDot({ id }: { id: string }) {
  const ch = chBy(id);
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span className="ch-dot" style={{ background: ch.color, width: 8, height: 8 }} />
      <span style={{ fontSize: 12, color: 'var(--ink-2)' }}>{ch.short}</span>
    </span>
  );
}

function Bar({
  value,
  max = 100,
  kind,
  width = 100,
}: {
  value: number;
  max?: number;
  kind?: 'ok' | 'warn' | 'bad' | 'accent';
  width?: number;
}) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  return (
    <div className={`bar ${kind ?? ''}`} style={{ width, flex: kind ? undefined : 1 }}>
      <i style={{ width: `${pct}%` }} />
    </div>
  );
}

function Sparkline({
  data,
  width = 120,
  height = 28,
  color = 'var(--ink-2)',
}: {
  data: number[];
  width?: number;
  height?: number;
  color?: string;
}) {
  if (data.length < 2) return <svg width={width} height={height} aria-hidden />;
  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1;
  const stepX = width / (data.length - 1);
  const pts = data
    .map(
      (v, i) =>
        `${(i * stepX).toFixed(1)},${(height - ((v - min) / range) * (height - 4) - 2).toFixed(1)}`,
    )
    .join(' ');
  const lastVal = data[data.length - 1]!;
  const lastY = height - ((lastVal - min) / range) * (height - 4) - 2;
  return (
    <svg width={width} height={height} style={{ display: 'block' }} aria-hidden>
      <polyline fill="none" stroke={color} strokeWidth="1.25" points={pts} />
      <circle cx={width} cy={lastY} r="2" fill={color} />
    </svg>
  );
}
