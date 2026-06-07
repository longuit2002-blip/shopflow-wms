/**
 * KPI stat row — Inventory header.
 *
 * Four figures across the top: on-hand, reserved, below-threshold,
 * oversell-risk. Rendered as a borderless stat row (label over tabular
 * number, hairline column dividers), NOT boxed cards — per DESIGN.md
 * "KPIs read as a compact stat row, not boxed". Color is spent only when
 * a count is a real alert (below-threshold / oversell-risk > 0); inactive
 * figures stay ink-neutral (no heavy color on inactive states).
 *
 * Loading: prior value holds at reduced opacity so the eye keeps its
 * anchor across the 2s poll rather than reflowing.
 */

import { useInventorySummaryQuery } from '../../hooks/useInventoryQuery';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

type Kind = 'neutral' | 'info' | 'warn' | 'bad';

export function KpiStrip() {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useInventorySummaryQuery();

  const stats: { label: string; value: number | null; kind: Kind }[] = [
    { label: t('Tồn thực', 'On hand'), value: data?.totalAvailable ?? null, kind: 'neutral' },
    { label: t('Đã giữ chỗ', 'Reserved'), value: data?.totalReserved ?? null, kind: 'info' },
    {
      label: t('Dưới mức an toàn', 'Below threshold'),
      value: data?.belowThresholdCount ?? null,
      kind: 'warn',
    },
    {
      label: t('Nguy cơ bán vượt', 'Oversell risk'),
      value: data?.oversellRiskCount ?? null,
      kind: 'bad',
    },
  ];

  return (
    <section
      aria-label={t('Chỉ số tồn kho', 'Inventory KPIs')}
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
  // Color a figure only when it is a real alert: a warn/bad count that is
  // actually non-zero. Everything else reads ink-neutral.
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
