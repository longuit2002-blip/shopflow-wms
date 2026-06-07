/**
 * OrdersKpiStrip — Orders header stat row.
 *
 * Four figures: active orders, awaiting-pick, awaiting-ship, failed-today.
 * Same borderless stat-row vocabulary as the Inventory `<KpiStrip>` (label
 * over tabular number, hairline column dividers, no boxed cards) per
 * DESIGN.md. Color fires only on a real alert (failed-today > 0); other
 * figures stay ink-neutral.
 *
 * Values from `useOrderKpiQuery()`; SignalR keeps the cache fresh with a
 * 2-s polling fallback when the hub is down (R13). camelCase wire shape.
 */

import { useOrderKpiQuery } from '../../hooks/useOrdersQuery';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

type Kind = 'neutral' | 'info' | 'warn' | 'bad';

export function OrdersKpiStrip() {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useOrderKpiQuery();

  const stats: { label: string; value: number | null; kind: Kind }[] = [
    {
      label: t('Đơn đang xử lý', 'Active orders'),
      value: data?.activeOrders ?? null,
      kind: 'neutral',
    },
    { label: t('Chờ soạn', 'Awaiting pick'), value: data?.awaitingPick ?? null, kind: 'info' },
    { label: t('Chờ giao', 'Awaiting ship'), value: data?.awaitingShip ?? null, kind: 'warn' },
    { label: t('Lỗi hôm nay', 'Failed today'), value: data?.failedToday ?? null, kind: 'bad' },
  ];

  return (
    <section
      aria-label={t('Chỉ số đơn hàng', 'Orders KPIs')}
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        padding: 'var(--s-4) var(--s-6)',
        borderBottom: '1px solid var(--line)',
        background: 'var(--panel)',
      }}
    >
      {stats.map((s, i) => (
        <KpiStat key={s.label} {...s} first={i === 0} isStale={isLoading} lang={lang} />
      ))}
      {isError && (
        <div
          role="alert"
          style={{
            flexBasis: '100%',
            marginTop: 'var(--s-3)',
            fontSize: 'var(--text-xs)',
            color: 'var(--bad-ink)',
          }}
        >
          {t('Không tải được KPI; sẽ thử lại.', 'Could not load KPIs; retrying.')}
        </div>
      )}
    </section>
  );
}

const ALERT_INK: Record<Kind, string> = {
  neutral: 'var(--ink)',
  info: 'var(--ink)',
  warn: 'var(--warn-ink)',
  bad: 'var(--bad-ink)',
};
const ALERT_DOT: Record<Kind, string> = {
  neutral: 'transparent',
  info: 'transparent',
  warn: 'var(--warn)',
  bad: 'var(--bad)',
};

function KpiStat({
  label,
  value,
  kind,
  first,
  isStale,
  lang,
}: {
  label: string;
  value: number | null;
  kind: Kind;
  first: boolean;
  isStale: boolean;
  lang: 'vi' | 'en';
}) {
  const isAlert = (kind === 'warn' || kind === 'bad') && value != null && value > 0;

  return (
    <div
      style={{
        flex: '1 1 0',
        minWidth: 0,
        padding: first ? '0 var(--s-5) 0 0' : '0 var(--s-5)',
        borderLeft: first ? 'none' : '1px solid var(--line)',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--s-1)',
      }}
    >
      <span
        style={{
          fontSize: 'var(--text-xs)',
          lineHeight: 'var(--lh-xs)',
          color: 'var(--ink-3)',
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {label}
      </span>
      <span
        className="tnum"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 'var(--s-2)',
          fontSize: 'var(--text-2xl)',
          lineHeight: 'var(--lh-2xl)',
          fontWeight: 600,
          color: isAlert ? ALERT_INK[kind] : 'var(--ink)',
          fontVariantNumeric: 'tabular-nums',
          opacity: value == null && isStale ? 0.45 : 1,
          transition: 'opacity var(--duration-fast) var(--ease-out)',
        }}
      >
        {isAlert && (
          <span
            aria-hidden="true"
            style={{
              width: 7,
              height: 7,
              borderRadius: '50%',
              background: ALERT_DOT[kind],
              flex: 'none',
            }}
          />
        )}
        {value != null ? fmtNum(value, lang) : '—'}
      </span>
    </div>
  );
}
