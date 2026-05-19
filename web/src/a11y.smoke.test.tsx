/**
 * A11y smoke test — Sprint-6 plan U13.
 *
 * Runs each of Sprint-6's headline surfaces through axe-core and asserts
 * zero violations. jsdom can't measure contrast (no real layout), so
 * these tests cover ARIA roles, label associations, and DOM-structure
 * a11y issues. Contrast lives in `tokens.css` §6.1 fixes; design tokens
 * are reviewed at canon time, not in unit tests.
 *
 * Add a new case here whenever a new top-level surface ships — the
 * harness is cheap and catches regressions like "added an icon button
 * without aria-label" or "input lost its <label htmlFor>".
 */

import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest';
import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { axe } from 'vitest-axe';
import type { ReactElement } from 'react';
import { LoginScreen } from './components/auth/LoginScreen';
import { SkuTable } from './components/inventory/SkuTable';
import { AdjustStockModal } from './components/inventory/AdjustStockModal';
import { CreateSkuModal } from './components/inventory/CreateSkuModal';
import { FlashSaleToggle } from './components/inventory/FlashSaleToggle';
import { ToastViewport } from './components/primitives/Toast';
import { SagaPipeline } from './components/orders/SagaPipeline';
import { TransitionsLog } from './components/orders/TransitionsLog';
import { OrderLineItems } from './components/orders/OrderLineItems';
import { useToast, __resetToastsForTests } from './hooks/useToast';
import { __resetLocaleForTests } from './hooks/useLocale';
import { __resetAuthForTests } from './hooks/useAuth';
import type { SkuListItem } from './api/inventory';
import type { OrderTransitionDto, OrderLineResponse } from './api/orders';

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({
    defaultOptions: { mutations: { retry: false }, queries: { retry: false } },
  });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const FIXTURE_ITEMS: SkuListItem[] = [
  {
    Sku: 'YN-RED-100',
    Available: 95,
    Reserved: 5,
    Name: 'Yến chưng đường phèn',
    Category: 'finished-goods',
    Threshold: 10,
    IsFlashSale: false,
    Allocations: [],
    P24Outbound: 0,
  },
  {
    Sku: 'YN-GOLD-200',
    Available: 5,
    Reserved: 10,
    Name: 'Yến chưng saffron',
    Category: 'finished-goods',
    Threshold: 20,
    IsFlashSale: true,
    Allocations: [],
    P24Outbound: 0,
  },
];

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

describe('A11y smoke — Sprint-6 surfaces', () => {
  it('LoginScreen has no axe violations', async () => {
    const { container } = render(<LoginScreen />);
    expect(await axe(container)).toHaveNoViolations();
  });

  it('SkuTable with both healthy + at-risk SKUs has no axe violations', async () => {
    const { container } = renderWithClient(
      <table>
        <SkuTable items={FIXTURE_ITEMS} onSelectSku={() => {}} selectedSku={null} />
      </table>,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('AdjustStockModal open has no axe violations', async () => {
    const { container } = renderWithClient(
      <AdjustStockModal isOpen onClose={() => {}} sku="YN-RED-100" />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('CreateSkuModal open has no axe violations', async () => {
    const { container } = renderWithClient(
      <CreateSkuModal isOpen onClose={() => {}} />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('FlashSaleToggle (both states) has no axe violations', async () => {
    const { container } = renderWithClient(
      <>
        <FlashSaleToggle sku="YN-RED-100" value={false} />
        <FlashSaleToggle sku="YN-GOLD-200" value={true} />
      </>,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('ToastViewport with one success + one error toast has no axe violations', async () => {
    useToast.getState().push({
      kind: 'success',
      title: 'Đã lưu',
      body: 'YN-001 cập nhật',
      durationMs: 0,
    });
    useToast.getState().push({
      kind: 'error',
      title: 'Lỗi',
      idempotencyKey: '01HXYZ',
      traceId: 'trace-abc',
      durationMs: 0,
    });
    const { container } = render(<ToastViewport />);
    expect(await axe(container)).toHaveNoViolations();
  });
});

const FIXTURE_TRANSITIONS: OrderTransitionDto[] = [
  {
    Id: '00000000-0000-0000-0000-000000000001',
    OrderId: '00000000-0000-0000-0000-000000000aaa',
    FromState: 'Initial',
    ToState: 'AwaitingReservation',
    OccurredAt: '2026-05-19T10:00:00.000Z',
    EventType: 'OrderPlacedV1',
    CorrelationId: '00-trace-1-01',
  },
  {
    Id: '00000000-0000-0000-0000-000000000002',
    OrderId: '00000000-0000-0000-0000-000000000aaa',
    FromState: 'AwaitingReservation',
    ToState: 'Reserved',
    OccurredAt: '2026-05-19T10:00:47.000Z',
    EventType: 'StockReservedV1',
    CorrelationId: '00-trace-2-01',
  },
  {
    Id: '00000000-0000-0000-0000-000000000003',
    OrderId: '00000000-0000-0000-0000-000000000aaa',
    FromState: 'Reserved',
    ToState: 'AwaitingPick',
    OccurredAt: '2026-05-19T10:01:30.000Z',
    EventType: 'StockReservedV1',
    CorrelationId: '00-trace-3-01',
  },
];

const FIXTURE_LINES: OrderLineResponse[] = [
  { Sku: 'YN-RED-100', Qty: 2, ExpectedWeight: 100 },
  { Sku: 'YN-GOLD-200', Qty: 1, ExpectedWeight: 50 },
  { Sku: 'YN-CLASSIC-50', Qty: 5, ExpectedWeight: null },
];

describe('A11y smoke — Sprint-7 Orders surfaces', () => {
  it('SagaPipeline (happy path + failure variant) has no axe violations', async () => {
    const { container } = render(
      <>
        <SagaPipeline
          currentState="AwaitingPick"
          transitions={FIXTURE_TRANSITIONS}
        />
        <SagaPipeline
          currentState="Cancelled"
          transitions={[
            ...FIXTURE_TRANSITIONS,
            {
              Id: '00000000-0000-0000-0000-000000000004',
              OrderId: '00000000-0000-0000-0000-000000000aaa',
              FromState: 'AwaitingReservation',
              ToState: 'CompensatingReservation',
              OccurredAt: '2026-05-19T10:02:00.000Z',
              EventType: 'StockReservationFailedV1',
              CorrelationId: '00-trace-fail-01',
            },
            {
              Id: '00000000-0000-0000-0000-000000000005',
              OrderId: '00000000-0000-0000-0000-000000000aaa',
              FromState: 'CompensatingReservation',
              ToState: 'Cancelled',
              OccurredAt: '2026-05-19T10:02:10.000Z',
              EventType: 'PathA_EmptyReleaseSet',
              CorrelationId: '00-trace-fail-02',
            },
          ]}
          failureCause="StockReservationFailedV1"
        />
      </>,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('TransitionsLog with multi-transition feed has no axe violations', async () => {
    const { container } = render(<TransitionsLog transitions={FIXTURE_TRANSITIONS} />);
    expect(await axe(container)).toHaveNoViolations();
  });

  it('TransitionsLog empty state has no axe violations', async () => {
    const { container } = render(<TransitionsLog transitions={[]} />);
    expect(await axe(container)).toHaveNoViolations();
  });

  it('OrderLineItems with 3 lines has no axe violations (KTD11 cell-level button)', async () => {
    const { container } = render(
      <OrderLineItems lines={FIXTURE_LINES} onLineClick={() => {}} />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });
});
