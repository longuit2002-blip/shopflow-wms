/**
 * KPI strip — Sprint-6 plan U9 / R5 / R21.
 *
 * Four cards across the top of the Inventory screen: total available,
 * total reserved, below-threshold count, oversell-risk count. Each
 * value is tabular-numeral so the eye reads them like a counter rather
 * than reflowing on each 2-second poll.
 *
 * Loading state: prev values stay rendered at 50% opacity so the eye
 * doesn't lose its anchor between polls (per STYLING_SPECS §7).
 */

import { useInventorySummaryQuery } from '../../hooks/useInventoryQuery';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

export function KpiStrip() {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useInventorySummaryQuery();

  const cards = [
    {
      label: t('Tồn thực', 'On hand'),
      value: data?.totalAvailable ?? null,
      kind: 'neutral' as const,
    },
    {
      label: t('Đã giữ chỗ', 'Reserved'),
      value: data?.totalReserved ?? null,
      kind: 'info' as const,
    },
    {
      label: t('Dưới mức an toàn', 'Below threshold'),
      value: data?.belowThresholdCount ?? null,
      kind: 'warn' as const,
    },
    {
      label: t('Nguy cơ bán vượt', 'Oversell risk'),
      value: data?.oversellRiskCount ?? null,
      kind: 'bad' as const,
    },
  ];

  return (
    <section
      aria-label={t('Chỉ số tồn kho', 'Inventory KPIs')}
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
