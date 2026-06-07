/**
 * TransitionsLog tests — Sprint-7 plan U12.
 *
 * Covers:
 *   1. Chronological sort: 5 transitions in chronological order render
 *      newest-first (most-recent at top).
 *   2. Elapsed-since-previous: per-row delta is rendered using the same
 *      shape SagaPipeline uses (`47s`, `45m`, …).
 *   3. Failure styling: the most-recent transition into `Cancelled`
 *      wears the `--bad-soft` background token (asserted via the row's
 *      `cancelled` class so we don't depend on jsdom computed styles).
 *   4. Empty state: `transitions=[]` renders the bilingual "No
 *      transitions yet" copy in both VI (default) and EN locales.
 *   5. A11y: axe-clean container; `aria-live="polite"` set on the feed.
 */

import { afterEach, beforeEach, describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import { TransitionsLog, type OrderTransitionDto } from './TransitionsLog';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

beforeEach(() => {
  __resetLocaleForTests();
});

afterEach(() => {
  __resetLocaleForTests();
});

const ORDER_ID = '01HORDER0000000000000000';

function makeTransition(
  partial: Partial<OrderTransitionDto> & { id: string; occurredAt: string },
): OrderTransitionDto {
  return {
    orderId: ORDER_ID,
    fromState: 'Placed',
    toState: 'Reserved',
    eventType: 'OrderPlacedV1',
    correlationId: 'corr-0001',
    ...partial,
  };
}

// Five transitions seeded in chronological order — newest is index 4.
const FIVE_IN_ORDER: OrderTransitionDto[] = [
  makeTransition({
    id: '01HA000000000000000000T1',
    fromState: 'Placed',
    toState: 'AwaitingReservation',
    occurredAt: '2026-05-19T10:00:00Z',
    eventType: 'OrderPlacedV1',
  }),
  makeTransition({
    id: '01HA000000000000000000T2',
    fromState: 'AwaitingReservation',
    toState: 'Reserved',
    occurredAt: '2026-05-19T10:00:47Z', // +47s
    eventType: 'StockReservedV1',
  }),
  makeTransition({
    id: '01HA000000000000000000T3',
    fromState: 'Reserved',
    toState: 'AwaitingPick',
    occurredAt: '2026-05-19T10:01:30Z', // +43s
    eventType: 'PickRequestQueuedV1',
  }),
  makeTransition({
    id: '01HA000000000000000000T4',
    fromState: 'AwaitingPick',
    toState: 'Picked',
    occurredAt: '2026-05-19T10:46:30Z', // +45m
    eventType: 'PickConfirmedV1',
  }),
  makeTransition({
    id: '01HA000000000000000000T5',
    fromState: 'Picked',
    toState: 'AwaitingPack',
    occurredAt: '2026-05-19T10:48:30Z', // +2m
    eventType: 'PackRequestedV1',
  }),
];

describe('TransitionsLog', () => {
  it('renders 5 chronological transitions newest-first', () => {
    render(<TransitionsLog transitions={FIVE_IN_ORDER} />);
    const rows = screen.getAllByTestId('transition-row');
    expect(rows).toHaveLength(5);

    // Most-recent at the top: T5 (Picked → AwaitingPack).
    expect(within(rows[0]!).getByText('Picked')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('AwaitingPack')).toBeInTheDocument();

    // Oldest at the bottom: T1 (Placed → AwaitingReservation).
    expect(within(rows[4]!).getByText('Placed')).toBeInTheDocument();
    expect(within(rows[4]!).getByText('AwaitingReservation')).toBeInTheDocument();

    // The event-type displays verbatim (Sprint-7 trade-off — no human
    // labels yet).
    expect(within(rows[0]!).getByText('PackRequestedV1')).toBeInTheDocument();
  });

  it('computes elapsed-since-previous correctly per row', () => {
    render(<TransitionsLog transitions={FIVE_IN_ORDER} />);
    const elapsedNodes = screen.getAllByTestId('transition-elapsed');

    // The oldest row (bottom) has no previous transition → no elapsed
    // badge rendered. So we expect 4 elapsed badges, not 5.
    expect(elapsedNodes).toHaveLength(4);

    // Newest-first order:
    //   row 0: T5, delta vs T4 = 2 minutes → "+2m"
    //   row 1: T4, delta vs T3 = 45 minutes → "+45m"
    //   row 2: T3, delta vs T2 = 43 seconds → "+43s"
    //   row 3: T2, delta vs T1 = 47 seconds → "+47s"
    expect(elapsedNodes[0]).toHaveTextContent('+2m');
    expect(elapsedNodes[1]).toHaveTextContent('+45m');
    expect(elapsedNodes[2]).toHaveTextContent('+43s');
    expect(elapsedNodes[3]).toHaveTextContent('+47s');
  });

  it('paints the most-recent row with bad-soft when ToState is Cancelled', () => {
    const cancelledFlow: OrderTransitionDto[] = [
      makeTransition({
        id: '01HC000000000000000000T1',
        fromState: 'Placed',
        toState: 'AwaitingReservation',
        occurredAt: '2026-05-19T11:00:00Z',
        eventType: 'OrderPlacedV1',
      }),
      makeTransition({
        id: '01HC000000000000000000T2',
        fromState: 'AwaitingReservation',
        toState: 'CompensatingReservation',
        occurredAt: '2026-05-19T11:00:05Z',
        eventType: 'StockReservationFailedV1',
      }),
      makeTransition({
        id: '01HC000000000000000000T3',
        fromState: 'CompensatingReservation',
        toState: 'Cancelled',
        occurredAt: '2026-05-19T11:00:10Z',
        eventType: 'OrderCancelledV1',
      }),
    ];

    render(<TransitionsLog transitions={cancelledFlow} />);
    const rows = screen.getAllByTestId('transition-row');

    // Newest row is the Cancelled one.
    expect(rows[0]).toHaveClass('cancelled');
    expect(rows[0]).toHaveAttribute('data-cancelled', 'true');
    // The bad-soft token shows up in the inline style declaration.
    expect(rows[0]!.getAttribute('style') ?? '').toContain('var(--bad-soft)');

    // Older rows are NOT flagged cancelled.
    expect(rows[1]).not.toHaveClass('cancelled');
    expect(rows[2]).not.toHaveClass('cancelled');
  });

  it('renders the empty-state copy in both locales', () => {
    // Default (vi).
    const { unmount } = render(<TransitionsLog transitions={[]} />);
    expect(screen.getByTestId('transitions-empty')).toHaveTextContent(
      'Chưa có chuyển trạng thái nào',
    );
    unmount();

    // English.
    setLang('en');
    render(<TransitionsLog transitions={[]} />);
    expect(screen.getByTestId('transitions-empty')).toHaveTextContent('No transitions yet');
  });

  it('container is axe-clean and exposes aria-live="polite"', async () => {
    const { container } = render(<TransitionsLog transitions={FIVE_IN_ORDER} />);

    const region = screen.getByTestId('transitions-log');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toHaveAttribute('aria-label');

    expect(await axe(container)).toHaveNoViolations();
  });
});
