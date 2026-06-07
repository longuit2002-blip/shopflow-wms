/**
 * StatRow — the canonical header stat-strip shell.
 *
 * One shell for every screen's top metrics so the vocabulary is identical
 * everywhere (DESIGN.md: "KPIs read as a compact stat row, not boxed").
 * Borderless: panel background, a single bottom hairline, and hairline
 * dividers between columns — no per-item cards. Each direct child becomes an
 * equal-width column; pass <StatItem> for the common label-over-number case,
 * or arbitrary content (icon + multi-line) for richer headers.
 *
 * Consumers: Inventory KpiStrip, Orders OrdersKpiStrip, Compliance header.
 */

import type { ReactNode } from 'react';
import { Children, isValidElement } from 'react';

export function StatRow({
  ariaLabel,
  children,
  footer,
}: {
  ariaLabel: string;
  children: ReactNode;
  /** Full-width row below the columns (e.g. an error alert). */
  footer?: ReactNode;
}) {
  const cols = Children.toArray(children).filter(isValidElement);
  return (
    <section
      aria-label={ariaLabel}
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        padding: 'var(--s-4) var(--s-6)',
        borderBottom: '1px solid var(--line)',
        background: 'var(--panel)',
      }}
    >
      {cols.map((child, i) => (
        <div
          key={i}
          style={{
            flex: '1 1 0',
            minWidth: 0,
            padding: i === 0 ? '0 var(--s-5) 0 0' : '0 var(--s-5)',
            borderLeft: i === 0 ? 'none' : '1px solid var(--line)',
          }}
        >
          {child}
        </div>
      ))}
      {footer}
    </section>
  );
}

export type StatKind = 'neutral' | 'info' | 'warn' | 'bad';

const ALERT_INK: Record<StatKind, string> = {
  neutral: 'var(--ink)',
  info: 'var(--ink)',
  warn: 'var(--warn-ink)',
  bad: 'var(--bad-ink)',
};
const ALERT_DOT: Record<StatKind, string> = {
  neutral: 'transparent',
  info: 'transparent',
  warn: 'var(--warn)',
  bad: 'var(--bad)',
};

/**
 * Label over a tabular figure. Color fires only on a real alert (a warn/bad
 * count that is actually non-zero, with a leading dot); everything else reads
 * ink-neutral — no heavy color on inactive states.
 */
export function StatItem({
  label,
  value,
  kind = 'neutral',
  isStale = false,
}: {
  label: string;
  /** Pre-formatted display value, or null while unavailable. */
  value: string | null;
  kind?: StatKind;
  isStale?: boolean;
}) {
  const isAlert = (kind === 'warn' || kind === 'bad') && value != null && value !== '0';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
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
        {value ?? '—'}
      </span>
    </div>
  );
}
