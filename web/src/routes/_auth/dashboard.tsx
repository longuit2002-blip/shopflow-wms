import { useEffect, useMemo, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { ChevronRight, ArrowUpRight } from 'lucide-react';
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
 * Layout: top live strip (per-tenant reservation p99 vs fleet median +
 * noisy-neighbour pill) → order pipeline card (5 saga stages, breach-tinted)
 * → manager body grid (active-SLA-breach table on the left; picker
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

// ── Mock tenant + channels ──────────────────────────────────────────────────

const TENANT = {
  db: 'shopflow_yensaokhanhhoa',
};

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
      <LiveStrip />
      <PipelineCard />
      <ManagerBody ageAdj={ageAdj} />
    </div>
  );
}

// ── Top live strip ───────────────────────────────────────────────────────---

function LiveStrip() {
  return (
    <div data-tour="fairness" className="strip" style={{ gap: 18 }}>
      <LiveDot
        kind="info"
        label={t('Trực tiếp', 'Live')}
        sub={t(
          `· ${HEALTH.signalrConns} kết nối signalr · tenant group ${TENANT.db}`,
          `· ${HEALTH.signalrConns} signalr conns · tenant group ${TENANT.db}`,
        )}
      />
      <span style={{ flex: 1 }} />
      <span className="lbl">{t('p99 giữ chỗ · tenant này', 'reserve p99 · this tenant')}</span>
      <span className="mono tnum" style={{ fontSize: 13, fontWeight: 600 }}>
        {HEALTH.tenantP99Ms}ms
      </span>
      <span style={{ color: 'var(--ink-4)' }}>·</span>
      <span className="lbl">{t('Trung vị toàn fleet', 'Fleet median')}</span>
      <span className="mono tnum" style={{ fontSize: 13, color: 'var(--ink-3)' }}>
        {HEALTH.fleetMedianMs}ms
      </span>
      <Pill kind="ok">{t('noisy-neighbour: ổn định', 'noisy-neighbour: stable')}</Pill>
    </div>
  );
}

function LiveDot({
  kind,
  label,
  sub,
}: {
  kind: 'ok' | 'warn' | 'bad' | 'info';
  label: string;
  sub?: string;
}) {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <span className={`live-dot ${kind}`} />
      <span style={{ fontSize: 11.5, color: 'var(--ink-2)', fontWeight: 500 }}>{label}</span>
      {sub && (
        <span className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
          {sub}
        </span>
      )}
    </span>
  );
}

// ── Pipeline card ────────────────────────────────────────────────────────---

function PipelineCard() {
  return (
    <div style={{ padding: '14px 18px 0' }}>
      <div className="lbl" style={{ marginBottom: 8 }}>
        {t('Quy trình đơn hàng · 60 phút gần nhất', 'Order pipeline · last 60 minutes')}
      </div>
      <div className="card" data-review="border-card" style={{ overflow: 'hidden' }}>
        <div style={{ display: 'flex', alignItems: 'stretch' }}>
          {PIPELINE.map((p, i) => {
            const breached = p.breach > 0;
            const sub = STAGE_SUB[p.stage];
            return (
              <div
                key={p.stage}
                style={{
                  flex: 1,
                  padding: '14px 16px',
                  background: breached ? 'var(--bad-soft)' : 'transparent',
                  borderRight: i < PIPELINE.length - 1 ? '1px solid var(--line)' : 'none',
                  position: 'relative',
                }}
              >
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                  }}
                >
                  <span
                    className="lbl"
                    style={{ color: breached ? 'var(--bad-ink)' : 'var(--ink-3)' }}
                  >
                    {t(STAGE_LABEL[p.stage], p.stage)}
                  </span>
                  {breached && (
                    <Pill kind="bad">
                      {p.breach} {t('vi phạm', 'breached')}
                    </Pill>
                  )}
                </div>
                <div
                  className="mono tnum"
                  style={{
                    fontSize: 28,
                    fontWeight: 600,
                    color: breached ? 'var(--bad-ink)' : 'var(--ink)',
                    letterSpacing: '-0.02em',
                  }}
                >
                  {p.count.toLocaleString()}
                </div>
                <div style={{ fontSize: 11, color: 'var(--ink-3)', marginTop: 2 }}>
                  {t(sub.vi, sub.en)}
                </div>
              </div>
            );
          })}
        </div>
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
            <th style={{ width: 60 }} aria-label={t('Mở đơn', 'Open order')} />
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
