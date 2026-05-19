/**
 * OrdersKpiStrip — Sprint-7 plan U10.
 *
 * Four cards across the top of the Orders screen. Mirrors the Sprint-6
 * `<KpiStrip>` in `components/inventory/KpiStrip.tsx` shape (card layout,
 * loading-stale opacity, locale-aware labels, tabular numerals).
 *
 * Values come from U8's `useOrderKpiQuery()` and SignalR keeps the cache
 * fresh; the 2-s polling fallback only fires when the hub is down (R13).
 *
 * Wire shape: camelCase fields (Sprint-7.5 U1/U2 wire normalisation).
 */

import { useOrderKpiQuery } from '../../hooks/useOrdersQuery';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

export function OrdersKpiStrip() {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useOrderKpiQuery();

  const cards = [
    {
      label: t('Đơn đang xử lý', 'Active orders'),
      value: data?.activeOrders ?? null,
      kind: 'neutral' as const,
    },
    {
      label: t('Chờ soạn', 'Awaiting pick'),
      value: data?.awaitingPick ?? null,
      kind: 'info' as const,
    },
    {
      label: t('Chờ giao', 'Awaiting ship'),
      value: data?.awaitingShip ?? null,
      kind: 'warn' as const,
    },
    {
      label: t('Lỗi hôm nay', 'Failed today'),
      value: data?.failedToday ?? null,
      kind: 'bad' as const,
    },
  ];

  return (
    <section
      aria-label={t('Chỉ số đơn hàng', 'Orders KPIs')}
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(4, 1fr)',
        gap: 'var(--s-3)',
        padding: 'var(--s-4) var(--s-6)',
        borderBottom: '1px solid var(--line)',
        background: 'var(--bg-soft)',
      }}
    >
      {cards.map((c) => (
        <KpiCard
          key={c.label}
          label={c.label}
          value={c.value}
          kind={c.kind}
          isStale={isLoading}
          lang={lang}
        />
      ))}
      {isError && (
        <div
          role="alert"
          className="t-xs"
          style={{ gridColumn: '1 / -1', color: 'var(--bad-ink)' }}
        >
          {t('Không tải được KPI; sẽ thử lại.', 'Could not load KPIs; retrying.')}
        </div>
      )}
    </section>
  );
}

interface KpiCardProps {
  label: string;
  value: number | null;
  kind: 'neutral' | 'info' | 'warn' | 'bad';
  isStale: boolean;
  lang: 'vi' | 'en';
}

const KIND_COLOR: Record<KpiCardProps['kind'], string> = {
  neutral: 'var(--ink)',
  info: 'var(--info-500)',
  warn: 'var(--warn-ink)',
  bad: 'var(--bad-ink)',
};

function KpiCard({ label, value, kind, isStale, lang }: KpiCardProps) {
  return (
    <div
      className="card"
      style={{
        padding: 'var(--s-4)',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--s-2)',
      }}
    >
      <div className="lbl">{label}</div>
      <div
        className="tnum"
        style={{
          fontSize: 'var(--text-3xl)',
          lineHeight: 'var(--lh-3xl)',
          fontWeight: 600,
          color: KIND_COLOR[kind],
          opacity: value == null && isStale ? 0.5 : 1,
          transition: 'opacity 150ms ease',
          fontVariantNumeric: 'tabular-nums',
        }}
      >
        {value != null ? fmtNum(value, lang) : '—'}
      </div>
    </div>
  );
}
