/**
 * TransitionsLog — Sprint-7 plan U12.
 *
 * Append-only feed of saga state transitions for a single order, sorted
 * newest-first. Each row shows: a human-relative timestamp (absolute on
 * hover via `title`), the `FromState → ToState` pair separated by a
 * Lucide arrow, the elapsed time since the chronologically previous
 * transition, and the originating event type in monospace.
 *
 * A11y (doc-review design-lens #9): the container is `aria-live="polite"`
 * so when a new transition row is prepended (push via SignalR in U13),
 * screen readers announce it without interrupting the user. `aria-label`
 * names the region so assistive tech can navigate to it.
 *
 * Sort + derived data are computed in-render via `useMemo` (React 19
 * pattern — no `useEffect` for derived state, per Sprint-6 KTD10).
 *
 * Wire shape: camelCase fields (Sprint-7.5 U1/U2 wire normalisation) —
 * matches the backend `OrderTransition` entity verbatim.
 *
 * Sprint-7 trade-off: `eventType` is displayed verbatim as the CLR class
 * name (e.g. `StockReservedV1`). Sprint-7.5+ polishes to human labels.
 *
 * Independence from `<SagaPipeline>` (U11): U11 ships its own elapsed-time
 * helper. To avoid coupling at the component-pair level, this file ships
 * its own ~10-line `formatElapsed` helper. They are intentionally
 * duplicated; a shared helper can be hoisted later if a third caller
 * appears.
 */

import { useMemo } from 'react';
import { ArrowRight } from 'lucide-react';
import { t, useLocale, type LocaleCode } from '../../hooks/useLocale';
import { fmtAge, fmtDateTime } from '../../lib/format';

export interface OrderTransitionDto {
  id: string;
  orderId: string;
  fromState: string;
  toState: string;
  /** ISO 8601 timestamp. */
  occurredAt: string;
  eventType: string;
  correlationId: string;
}

export interface TransitionsLogProps {
  transitions: OrderTransitionDto[];
}

/**
 * Format an elapsed millisecond delta as `47s` / `1.2s` / `45m` etc.
 * Mirrors the shape that `<SagaPipeline>` (U11) uses on its segment
 * badges so both surfaces read consistently.
 */
function formatElapsed(deltaMs: number): string {
  if (deltaMs < 1000) return `${Math.max(0, Math.round(deltaMs))}ms`;
  const seconds = deltaMs / 1000;
  if (seconds < 60) {
    // Whole seconds for >=10s; one decimal under 10s to keep useful precision.
    return seconds >= 10 ? `${Math.round(seconds)}s` : `${seconds.toFixed(1)}s`;
  }
  const minutes = deltaMs / 60_000;
  if (minutes < 60) return `${Math.round(minutes)}m`;
  return `${(deltaMs / 3_600_000).toFixed(1)}h`;
}

export function TransitionsLog({ transitions }: TransitionsLogProps) {
  const { lang } = useLocale();

  // Sort newest-first in render via useMemo so we never store derived
  // state. The comparator is stable; tie-breaking by id keeps order
  // deterministic for transitions that share an occurredAt.
  const sorted = useMemo(() => {
    return [...transitions].sort((a, b) => {
      const at = Date.parse(a.occurredAt);
      const bt = Date.parse(b.occurredAt);
      if (bt !== at) return bt - at;
      return a.id.localeCompare(b.id);
    });
  }, [transitions]);

  return (
    <section
      className="transitions-log"
      aria-live="polite"
      aria-label={t('Lịch sử chuyển trạng thái', 'Saga transition history')}
      data-testid="transitions-log"
    >
      {sorted.length === 0 ? (
        <EmptyState />
      ) : (
        sorted.map((row, index) => {
          // The chronologically previous transition is the next item in
          // the newest-first array (i.e. index + 1).
          const previous = sorted[index + 1] ?? null;
          // The "last transition" in the failure-styling sense is the
          // most-recent one — index 0 in the sorted list.
          const isMostRecent = index === 0;
          return (
            <TransitionRow
              key={row.id}
              row={row}
              previous={previous}
              lang={lang}
              isMostRecent={isMostRecent}
            />
          );
        })
      )}
    </section>
  );
}

interface TransitionRowProps {
  row: OrderTransitionDto;
  previous: OrderTransitionDto | null;
  lang: LocaleCode;
  isMostRecent: boolean;
}

function TransitionRow({ row, previous, lang, isMostRecent }: TransitionRowProps) {
  const elapsedLabel = useMemo(() => {
    if (!previous) return null;
    const delta = Date.parse(row.occurredAt) - Date.parse(previous.occurredAt);
    if (!Number.isFinite(delta) || delta < 0) return null;
    return formatElapsed(delta);
  }, [previous, row.occurredAt]);

  const isCancelledTerminal = isMostRecent && row.toState === 'Cancelled';

  return (
    <article
      className={isCancelledTerminal ? 'transition-row cancelled' : 'transition-row'}
      data-testid="transition-row"
      data-cancelled={isCancelledTerminal ? 'true' : undefined}
      style={{
        display: 'grid',
        gridTemplateColumns: 'auto 1fr auto',
        gap: 'var(--s-3)',
        alignItems: 'center',
        padding: 'var(--s-3) var(--s-4)',
        borderBottom: '1px solid var(--neutral-200)',
        background: isCancelledTerminal ? 'var(--bad-soft)' : undefined,
      }}
    >
      <div
        className="t-xs tnum"
        title={fmtDateTime(row.occurredAt)}
        style={{ color: 'var(--neutral-500)', whiteSpace: 'nowrap' }}
      >
        {fmtAge(row.occurredAt, lang)}
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--s-2)',
          flexWrap: 'wrap',
        }}
      >
        <span className="t-sm" style={{ color: 'var(--ink-2)' }}>
          {row.fromState}
        </span>
        <ArrowRight
          size={14}
          aria-hidden="true"
          style={{ color: 'var(--neutral-500)', flexShrink: 0 }}
        />
        <span
          className="t-sm"
          style={{
            fontWeight: 600,
            color: isCancelledTerminal ? 'var(--bad-ink)' : 'var(--ink)',
          }}
        >
          {row.toState}
        </span>
        <span
          className="t-xs mono"
          data-testid="transition-event-type"
          style={{ color: 'var(--neutral-500)', marginLeft: 'var(--s-2)' }}
        >
          {row.eventType}
        </span>
      </div>

      {elapsedLabel ? (
        <div
          className="t-xs tnum"
          data-testid="transition-elapsed"
          style={{ color: 'var(--neutral-500)', whiteSpace: 'nowrap' }}
        >
          {t('+', '+')}
          {elapsedLabel}
        </div>
      ) : (
        <div aria-hidden="true" />
      )}
    </article>
  );
}

function EmptyState() {
  useLocale();
  return (
    <div
      data-testid="transitions-empty"
      style={{
        padding: 'var(--s-5)',
        color: 'var(--ink-2)',
        textAlign: 'center',
      }}
    >
      {t('Chưa có chuyển trạng thái nào', 'No transitions yet')}
    </div>
  );
}
