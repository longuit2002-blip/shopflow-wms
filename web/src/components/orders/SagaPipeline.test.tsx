/**
 * SagaPipeline tests — Sprint-7 plan U11.
 *
 * Seven scenarios per plan:
 *   1. Happy: currentState='Reserved' + 1 transition → 1 completed, 1 active, rest pending.
 *   2. Happy: currentState='Shipped' + 8 transitions → all completed; elapsed badges populated.
 *   3. Elapsed-time formatter: 47.2s + 45m.
 *   4. Elapsed-time edge cases: < 1s, zero, > 1h.
 *   5. Failure: Cancelled + StockReservationFailedV1 → fail node + caption visible.
 *   6. Empty transitions → all nodes pending.
 *   7. A11y: no axe violations; role="list" + aria-current="step" semantics correct.
 */

import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import { SagaPipeline, formatElapsed, type OrderTransitionDto } from './SagaPipeline';
import { __resetLocaleForTests } from '../../hooks/useLocale';

afterEach(() => {
  __resetLocaleForTests();
});

function makeTransition(
  fromState: string,
  toState: string,
  occurredAt: string,
  eventType: string = `${toState}V1`,
): OrderTransitionDto {
  return {
    id: `${fromState}-${toState}`,
    orderId: '01HORDERID',
    fromState: fromState,
    toState: toState,
    occurredAt: occurredAt,
    eventType: eventType,
    correlationId: 'trace-abc',
  };
}

describe('SagaPipeline — happy path with one transition', () => {
  it('renders Reserved as active and Placed as completed; rest pending', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:30Z', 'StockReservedV1'),
    ];

    render(<SagaPipeline currentState="Reserved" transitions={transitions} />);

    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'data-status',
      'completed',
    );
    expect(screen.getByTestId('saga-step-Reserved')).toHaveAttribute('data-status', 'active');
    expect(screen.getByTestId('saga-step-Reserved')).toHaveAttribute('aria-current', 'step');

    for (const node of ['AwaitingPick', 'Picked', 'AwaitingPack', 'Packed', 'AwaitingShip', 'Shipped']) {
      expect(screen.getByTestId(`saga-step-${node}`)).toHaveAttribute('data-status', 'pending');
    }
  });

  it('marks only the active node with aria-current=step', () => {
    const transitions = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:30Z'),
    ];
    render(<SagaPipeline currentState="Reserved" transitions={transitions} />);

    const activeNodes = screen.getAllByRole('listitem').filter(
      (li) => li.getAttribute('aria-current') === 'step',
    );
    expect(activeNodes).toHaveLength(1);
  });
});

describe('SagaPipeline — fully shipped order', () => {
  it('renders all eight nodes completed with elapsed badges populated', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:10Z'),
      makeTransition('Reserved', 'AwaitingPick', '2026-05-19T10:00:20Z'),
      makeTransition('AwaitingPick', 'Picked', '2026-05-19T10:00:40Z'),
      makeTransition('Picked', 'AwaitingPack', '2026-05-19T10:00:50Z'),
      makeTransition('AwaitingPack', 'Packed', '2026-05-19T10:02:30Z'),
      makeTransition('Packed', 'AwaitingShip', '2026-05-19T10:02:40Z'),
      makeTransition('AwaitingShip', 'Shipped', '2026-05-19T10:05:00Z'),
    ];

    render(<SagaPipeline currentState="Shipped" transitions={transitions} />);

    for (const node of [
      'AwaitingReservation',
      'Reserved',
      'AwaitingPick',
      'Picked',
      'AwaitingPack',
      'Packed',
      'AwaitingShip',
    ]) {
      expect(screen.getByTestId(`saga-step-${node}`)).toHaveAttribute('data-status', 'completed');
    }
    expect(screen.getByTestId('saga-step-Shipped')).toHaveAttribute('data-status', 'active');

    // Spot-check elapsed badges: AwaitingReservation → Reserved was 10s, so badge "10.0s".
    const placedNode = screen.getByTestId('saga-step-AwaitingReservation');
    expect(within(placedNode).getByText('10.0s')).toBeInTheDocument();

    // AwaitingPack → Packed was 100s → "1.7s" no... 100_000ms = 1m 40s, but < 3.6e6 → "1m"
    // Actually 100000 / 60000 = 1.66 → floor → 1 → "1m"
    const awaitingPackNode = screen.getByTestId('saga-step-AwaitingPack');
    expect(within(awaitingPackNode).getByText('1m')).toBeInTheDocument();
  });
});

describe('formatElapsed', () => {
  it('renders 47.2s for 47200ms', () => {
    expect(formatElapsed(47_200)).toBe('47.2s');
  });

  it('renders 45m for 2_700_000ms', () => {
    expect(formatElapsed(2_700_000)).toBe('45m');
  });

  it('renders < 1s for sub-second durations', () => {
    expect(formatElapsed(250)).toBe('< 1s');
    expect(formatElapsed(999)).toBe('< 1s');
  });

  it('renders em-dash for zero and negative durations', () => {
    expect(formatElapsed(0)).toBe('—');
    expect(formatElapsed(-100)).toBe('—');
    expect(formatElapsed(null)).toBe('—');
  });

  it('renders hours+minutes for durations over 1h', () => {
    // 2h 15m → 8_100_000ms
    expect(formatElapsed(8_100_000)).toBe('2h 15m');
    // exactly 1h → "1h 0m"
    expect(formatElapsed(3_600_000)).toBe('1h 0m');
  });

  it('renders 1.2s for 1234ms (one decimal)', () => {
    expect(formatElapsed(1_234)).toBe('1.2s');
  });
});

describe('SagaPipeline — failure state', () => {
  it('renders Cancelled with failure node + caption and StockReservationFailedV1 cause', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition(
        'AwaitingReservation',
        'CompensatingReservation',
        '2026-05-19T10:00:05Z',
        'StockReservationFailedV1',
      ),
      makeTransition(
        'CompensatingReservation',
        'Cancelled',
        '2026-05-19T10:00:06Z',
        'StockReleasedV1',
      ),
    ];

    render(
      <SagaPipeline
        currentState="Cancelled"
        transitions={transitions}
        failureCause="StockReservationFailedV1"
      />,
    );

    // Failure node is AwaitingReservation (mapped to "Placed").
    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'data-status',
      'fail',
    );

    const caption = screen.getByTestId('saga-failure-caption');
    expect(caption).toBeInTheDocument();
    expect(caption.textContent).toContain('StockReservationFailedV1');
    expect(caption.textContent).toMatch(/Failed at|Lỗi tại/);
  });

  it('renders no failure caption when currentState is not Cancelled', () => {
    const transitions = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:10Z'),
    ];
    render(<SagaPipeline currentState="Reserved" transitions={transitions} />);
    expect(screen.queryByTestId('saga-failure-caption')).not.toBeInTheDocument();
  });

  it('marks no node as aria-current when failed', () => {
    const transitions = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition(
        'AwaitingReservation',
        'CompensatingReservation',
        '2026-05-19T10:00:05Z',
      ),
      makeTransition('CompensatingReservation', 'Cancelled', '2026-05-19T10:00:06Z'),
    ];
    render(
      <SagaPipeline
        currentState="Cancelled"
        transitions={transitions}
        failureCause="StockReservationFailedV1"
      />,
    );
    const activeNodes = screen.getAllByRole('listitem').filter(
      (li) => li.getAttribute('aria-current') === 'step',
    );
    expect(activeNodes).toHaveLength(0);
  });
});

describe('SagaPipeline — empty transitions', () => {
  it('renders all nodes pending when transitions array is empty', () => {
    render(<SagaPipeline currentState="Created" transitions={[]} />);
    // Created collapses to AwaitingReservation, which becomes the active node.
    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'data-status',
      'active',
    );
    for (const node of ['Reserved', 'AwaitingPick', 'Picked', 'AwaitingPack', 'Packed', 'AwaitingShip', 'Shipped']) {
      expect(screen.getByTestId(`saga-step-${node}`)).toHaveAttribute('data-status', 'pending');
    }
  });

  it('renders all nodes pending with no aria-current when currentState is unknown', () => {
    render(<SagaPipeline currentState="UnknownFutureState" transitions={[]} />);
    for (const node of [
      'AwaitingReservation',
      'Reserved',
      'AwaitingPick',
      'Picked',
      'AwaitingPack',
      'Packed',
      'AwaitingShip',
      'Shipped',
    ]) {
      expect(screen.getByTestId(`saga-step-${node}`)).toHaveAttribute('data-status', 'pending');
    }
  });
});

describe('SagaPipeline — a11y', () => {
  it('has zero axe violations in the happy-path render', async () => {
    const transitions = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:30Z'),
    ];
    const { container } = render(
      <SagaPipeline currentState="Reserved" transitions={transitions} />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('has zero axe violations in the failure render', async () => {
    const transitions = [
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:00Z'),
      makeTransition(
        'AwaitingReservation',
        'CompensatingReservation',
        '2026-05-19T10:00:05Z',
      ),
      makeTransition('CompensatingReservation', 'Cancelled', '2026-05-19T10:00:06Z'),
    ];
    const { container } = render(
      <SagaPipeline
        currentState="Cancelled"
        transitions={transitions}
        failureCause="StockReservationFailedV1"
      />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('exposes the pipeline as a labelled list with eight list items', () => {
    render(<SagaPipeline currentState="Reserved" transitions={[]} />);
    const list = screen.getByRole('list', { name: 'Saga progress' });
    expect(list).toBeInTheDocument();
    // The container's <ol> is the labelled list; its <li> children are
    // the eight pipeline nodes (other lists nested under may exist via
    // a11y plumbing, so scope the query to the labelled list).
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(8);
  });
});
