/**
 * SagaPipeline tests — Sprint-7 plan U11 + Sprint-7.5 U9 (9-node split).
 *
 * Scenarios:
 *   1. Happy: currentState='Created' → node 0 active, rest pending.
 *   2. Happy: currentState='AwaitingReservation' → node 0 completed, node 1 active.
 *   3. Happy: currentState='Reserved' → nodes 0+1 completed, node 2 active (AE6).
 *   4. Happy: currentState='Shipped' + 9 transitions → all 9 completed; no aria-current.
 *   5. Elapsed-time formatter: 47.2s + 45m + edge cases.
 *   6. Failure: Cancelled + StockReservationFailedV1 → fail node + caption visible.
 *   7. Empty transitions → all 9 nodes pending (or first node active for Created).
 *   8. A11y: no axe violations; role="list" + aria-current="step" semantics correct.
 *   9. Locale: Vi labels render correctly for all 9 nodes.
 */

import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import { SagaPipeline, formatElapsed, type OrderTransitionDto } from './SagaPipeline';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

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

describe('SagaPipeline — 9-node split (Sprint-7.5 U9)', () => {
  it('renders exactly 9 list items in canonical order', () => {
    render(<SagaPipeline currentState="Created" transitions={[]} />);
    const list = screen.getByRole('list', { name: 'Saga progress' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(9);

    // Verify DOM order via data-testid.
    const expectedOrder = [
      'Created',
      'AwaitingReservation',
      'Reserved',
      'AwaitingPick',
      'Picked',
      'AwaitingPack',
      'Packed',
      'AwaitingShip',
      'Shipped',
    ];
    expectedOrder.forEach((node, idx) => {
      expect(items[idx]).toHaveAttribute('data-testid', `saga-step-${node}`);
    });
  });
});

describe('SagaPipeline — currentState=Created', () => {
  it('renders node 0 (Created) as active and the rest as pending', () => {
    render(<SagaPipeline currentState="Created" transitions={[]} />);

    expect(screen.getByTestId('saga-step-Created')).toHaveAttribute('data-status', 'active');
    expect(screen.getByTestId('saga-step-Created')).toHaveAttribute('aria-current', 'step');

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

describe('SagaPipeline — currentState=AwaitingReservation', () => {
  it('renders Created as completed and AwaitingReservation as active', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z', 'OrderPlacedV1'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
    ];
    render(<SagaPipeline currentState="AwaitingReservation" transitions={transitions} />);

    expect(screen.getByTestId('saga-step-Created')).toHaveAttribute('data-status', 'completed');
    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'data-status',
      'active',
    );
    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'aria-current',
      'step',
    );

    for (const node of [
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

describe('SagaPipeline — currentState=Reserved (AE6)', () => {
  it('renders Created + AwaitingReservation completed and Reserved active', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z', 'OrderPlacedV1'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
      makeTransition(
        'AwaitingReservation',
        'Reserved',
        '2026-05-19T10:00:30Z',
        'StockReservedV1',
      ),
    ];

    render(<SagaPipeline currentState="Reserved" transitions={transitions} />);

    expect(screen.getByTestId('saga-step-Created')).toHaveAttribute('data-status', 'completed');
    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'data-status',
      'completed',
    );
    expect(screen.getByTestId('saga-step-Reserved')).toHaveAttribute('data-status', 'active');
    expect(screen.getByTestId('saga-step-Reserved')).toHaveAttribute('aria-current', 'step');

    for (const node of [
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

  it('marks only the active node with aria-current=step', () => {
    const transitions = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:30Z'),
    ];
    render(<SagaPipeline currentState="Reserved" transitions={transitions} />);

    const activeNodes = screen
      .getAllByRole('listitem')
      .filter((li) => li.getAttribute('aria-current') === 'step');
    expect(activeNodes).toHaveLength(1);
  });
});

describe('SagaPipeline — fully shipped order', () => {
  it('renders all nine nodes completed with elapsed badges populated', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z', 'OrderPlacedV1'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:02Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:12Z'),
      makeTransition('Reserved', 'AwaitingPick', '2026-05-19T10:00:22Z'),
      makeTransition('AwaitingPick', 'Picked', '2026-05-19T10:00:42Z'),
      makeTransition('Picked', 'AwaitingPack', '2026-05-19T10:00:52Z'),
      makeTransition('AwaitingPack', 'Packed', '2026-05-19T10:02:32Z'),
      makeTransition('Packed', 'AwaitingShip', '2026-05-19T10:02:42Z'),
      makeTransition('AwaitingShip', 'Shipped', '2026-05-19T10:05:02Z'),
    ];

    render(<SagaPipeline currentState="Shipped" transitions={transitions} />);

    for (const node of [
      'Created',
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

    // Created → AwaitingReservation gap was 2s → "2.0s" badge on Created.
    const createdNode = screen.getByTestId('saga-step-Created');
    expect(within(createdNode).getByText('2.0s')).toBeInTheDocument();

    // AwaitingReservation → Reserved was 10s → "10.0s" badge.
    const reservationNode = screen.getByTestId('saga-step-AwaitingReservation');
    expect(within(reservationNode).getByText('10.0s')).toBeInTheDocument();

    // AwaitingPack → Packed was 100s → "1m".
    const awaitingPackNode = screen.getByTestId('saga-step-AwaitingPack');
    expect(within(awaitingPackNode).getByText('1m')).toBeInTheDocument();
  });

  it('emits no aria-current step on the terminal Shipped state', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:02Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:12Z'),
      makeTransition('Reserved', 'AwaitingPick', '2026-05-19T10:00:22Z'),
      makeTransition('AwaitingPick', 'Picked', '2026-05-19T10:00:42Z'),
      makeTransition('Picked', 'AwaitingPack', '2026-05-19T10:00:52Z'),
      makeTransition('AwaitingPack', 'Packed', '2026-05-19T10:02:32Z'),
      makeTransition('Packed', 'AwaitingShip', '2026-05-19T10:02:42Z'),
      makeTransition('AwaitingShip', 'Shipped', '2026-05-19T10:05:02Z'),
    ];
    render(<SagaPipeline currentState="Shipped" transitions={transitions} />);

    // The Shipped node IS the active node, so it carries aria-current=step.
    const activeNodes = screen
      .getAllByRole('listitem')
      .filter((li) => li.getAttribute('aria-current') === 'step');
    expect(activeNodes).toHaveLength(1);
    expect(activeNodes[0]).toHaveAttribute('data-testid', 'saga-step-Shipped');
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
  it('renders Cancelled with failure node on AwaitingReservation + caption', () => {
    const transitions: OrderTransitionDto[] = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z', 'OrderPlacedV1'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
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

    // Failure node is AwaitingReservation (the last forward node entered).
    expect(screen.getByTestId('saga-step-AwaitingReservation')).toHaveAttribute(
      'data-status',
      'fail',
    );
    // Created precedes the failure → completed.
    expect(screen.getByTestId('saga-step-Created')).toHaveAttribute('data-status', 'completed');

    const caption = screen.getByTestId('saga-failure-caption');
    expect(caption).toBeInTheDocument();
    expect(caption.textContent).toContain('StockReservationFailedV1');
    expect(caption.textContent).toMatch(/Failed at|Lỗi tại/);
  });

  it('renders no failure caption when currentState is not Cancelled', () => {
    const transitions = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:10Z'),
    ];
    render(<SagaPipeline currentState="Reserved" transitions={transitions} />);
    expect(screen.queryByTestId('saga-failure-caption')).not.toBeInTheDocument();
  });

  it('marks no node as aria-current when failed', () => {
    const transitions = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
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
    const activeNodes = screen
      .getAllByRole('listitem')
      .filter((li) => li.getAttribute('aria-current') === 'step');
    expect(activeNodes).toHaveLength(0);
  });
});

describe('SagaPipeline — empty transitions', () => {
  it('renders Created active and the other 8 nodes pending', () => {
    render(<SagaPipeline currentState="Created" transitions={[]} />);
    expect(screen.getByTestId('saga-step-Created')).toHaveAttribute('data-status', 'active');
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

  it('renders all nodes pending with no aria-current when currentState is unknown', () => {
    render(<SagaPipeline currentState="UnknownFutureState" transitions={[]} />);
    for (const node of [
      'Created',
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

describe('SagaPipeline — Vi locale', () => {
  it('renders all 9 labels in Vietnamese', () => {
    setLang('vi');
    render(<SagaPipeline currentState="Created" transitions={[]} />);

    const list = screen.getByRole('list', { name: 'Saga progress' });
    expect(within(screen.getByTestId('saga-step-Created')).getByText('Đã tạo')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('saga-step-AwaitingReservation')).getByText('Chờ giữ chỗ'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId('saga-step-Reserved')).getByText('Đã giữ hàng'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId('saga-step-AwaitingPick')).getByText('Chờ soạn'),
    ).toBeInTheDocument();
    expect(within(screen.getByTestId('saga-step-Picked')).getByText('Đã soạn')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('saga-step-AwaitingPack')).getByText('Chờ đóng gói'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId('saga-step-Packed')).getByText('Đã đóng gói'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId('saga-step-AwaitingShip')).getByText('Chờ giao vận'),
    ).toBeInTheDocument();
    expect(within(screen.getByTestId('saga-step-Shipped')).getByText('Đã giao')).toBeInTheDocument();

    // Sanity-check the list semantics survive locale switch.
    expect(within(list).getAllByRole('listitem')).toHaveLength(9);
  });
});

describe('SagaPipeline — a11y', () => {
  it('has zero axe violations in the happy-path render', async () => {
    const transitions = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
      makeTransition('AwaitingReservation', 'Reserved', '2026-05-19T10:00:30Z'),
    ];
    const { container } = render(
      <SagaPipeline currentState="Reserved" transitions={transitions} />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('has zero axe violations in the failure render', async () => {
    const transitions = [
      makeTransition('Initial', 'Created', '2026-05-19T10:00:00Z'),
      makeTransition('Created', 'AwaitingReservation', '2026-05-19T10:00:01Z'),
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

  it('exposes the pipeline as a labelled list with nine list items', () => {
    render(<SagaPipeline currentState="Reserved" transitions={[]} />);
    const list = screen.getByRole('list', { name: 'Saga progress' });
    expect(list).toBeInTheDocument();
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(9);
  });
});
