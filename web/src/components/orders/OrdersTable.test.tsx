/**
 * OrdersTable tests — Sprint-7 plan U10.
 *
 * Mocks the U8 `useOrdersListQuery` hook + TanStack Router's `useNavigate`
 * so the component under test exercises pure render/click behaviour. The
 * underlying TanStack Query plumbing is covered by `useOrdersQuery.test.tsx`.
 *
 * Scenarios:
 *   1. 3-row response renders 3 rows with correct cell content.
 *   2. Click row's button → navigate to /orders/$orderId with row id.
 *   3. Status pill colour matches saga state via Pill `kind` class.
 *   4. Filter prop change → table re-renders with new filter (re-query).
 *   5. Empty state renders bilingual copy when Items.length === 0.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import type { OrderListItemDto, OrdersFilter } from '../../api/orders';

// ── Mocks ────────────────────────────────────────────────────────────────

interface QueryReturn {
  data?: { Items: OrderListItemDto[]; TotalCount: number };
  isLoading: boolean;
  isError: boolean;
}

const ordersRef = vi.hoisted(() => ({
  current: {
    data: undefined as QueryReturn['data'],
    isLoading: false,
    isError: false,
    lastFilter: undefined as OrdersFilter | undefined,
  },
}));

vi.mock('../../hooks/useOrdersQuery', () => {
  return {
    useOrdersListQuery: (filter: OrdersFilter) => {
      ordersRef.current.lastFilter = filter;
      return {
        data: ordersRef.current.data,
        isLoading: ordersRef.current.isLoading,
        isError: ordersRef.current.isError,
      } as QueryReturn;
    },
    // The component file only uses useOrdersListQuery, but other tests in
    // the same vitest project might import siblings; re-export defaults so
    // the mock is drop-in.
    useOrderKpiQuery: () => ({ data: undefined, isLoading: false, isError: false }),
    useOrderDetailQuery: () => ({ data: undefined, isLoading: false, isError: false }),
    useOrderTransitionsQuery: () => ({ data: undefined, isLoading: false, isError: false }),
  };
});

const navigateMock = vi.hoisted(() => vi.fn());

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>(
    '@tanstack/react-router',
  );
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

// Imports must come after vi.mock declarations.
import { OrdersTable } from './OrdersTable';

// ── Helpers ──────────────────────────────────────────────────────────────

function wrap(children: ReactNode) {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });
  return render(<QueryClientProvider client={qc}>{children}</QueryClientProvider>);
}

function row(overrides: Partial<OrderListItemDto> = {}): OrderListItemDto {
  return {
    Id: '01HABC0001',
    ChannelExternalOrderId: 'SHOPEE_ORDER_001',
    Channel: 'Shopee',
    LineCount: 3,
    CurrentSagaState: 'Reserved',
    Age: '00:15:00',
    LastTransitionAt: '2026-05-19T10:00:00Z',
    ...overrides,
  };
}

const FIXTURE_3: OrderListItemDto[] = [
  row({ Id: '01HABC0001', ChannelExternalOrderId: 'SHOPEE_ORDER_001' }),
  row({
    Id: '01HABC0002',
    ChannelExternalOrderId: 'LAZADA_ORDER_002',
    Channel: 'Lazada',
    CurrentSagaState: 'Shipped',
    LineCount: 1,
  }),
  row({
    Id: '01HABC0003',
    ChannelExternalOrderId: 'DIRECT_ORDER_003',
    Channel: 'Direct',
    CurrentSagaState: 'Cancelled',
    LineCount: 5,
  }),
];

beforeEach(() => {
  __resetLocaleForTests();
  ordersRef.current = {
    data: undefined,
    isLoading: false,
    isError: false,
    lastFilter: undefined,
  };
  navigateMock.mockReset();
});

afterEach(() => {
  __resetLocaleForTests();
});

// ── Scenarios ────────────────────────────────────────────────────────────

describe('OrdersTable', () => {
  it('renders 3 rows with correct external-order-id cell content', () => {
    ordersRef.current.data = { Items: FIXTURE_3, TotalCount: 3 };
    wrap(<OrdersTable filter={{}} />);

    expect(screen.getByText('SHOPEE_ORDER_001')).toBeInTheDocument();
    expect(screen.getByText('LAZADA_ORDER_002')).toBeInTheDocument();
    expect(screen.getByText('DIRECT_ORDER_003')).toBeInTheDocument();

    // Channel column entries.
    expect(screen.getByText('Shopee')).toBeInTheDocument();
    expect(screen.getByText('Lazada')).toBeInTheDocument();
    expect(screen.getByText('Direct')).toBeInTheDocument();
  });

  it("clicking a row's button navigates to /orders/$orderId with the row id", async () => {
    const user = userEvent.setup();
    ordersRef.current.data = { Items: FIXTURE_3, TotalCount: 3 };
    wrap(<OrdersTable filter={{}} />);

    await user.click(screen.getByTestId('order-row-01HABC0002'));

    expect(navigateMock).toHaveBeenCalledTimes(1);
    expect(navigateMock).toHaveBeenCalledWith({
      to: '/orders/$orderId',
      params: { orderId: '01HABC0002' },
    });
  });

  it('status pill colour matches saga state (Shipped → ok, Cancelled → bad, Reserved → info)', () => {
    ordersRef.current.data = { Items: FIXTURE_3, TotalCount: 3 };
    const { container } = wrap(<OrdersTable filter={{}} />);

    const shippedPill = screen.getByText('Shipped');
    const cancelledPill = screen.getByText('Cancelled');
    const reservedPill = screen.getByText('Reserved');

    expect(shippedPill.className).toMatch(/\bok\b/);
    expect(cancelledPill.className).toMatch(/\bbad\b/);
    expect(reservedPill.className).toMatch(/\binfo\b/);
    // Sanity: every pill carries the canon `pill` class.
    expect(container.querySelectorAll('.pill').length).toBe(3);
  });

  it('filter prop change → underlying hook receives the new filter object', () => {
    ordersRef.current.data = { Items: FIXTURE_3, TotalCount: 3 };

    const { rerender } = wrap(<OrdersTable filter={{}} />);
    expect(ordersRef.current.lastFilter).toEqual({});

    rerender(<OrdersTable filter={{ status: 'AwaitingPick', channel: 'SHOPEE' }} />);
    expect(ordersRef.current.lastFilter).toEqual({
      status: 'AwaitingPick',
      channel: 'SHOPEE',
    });
  });

  it('renders the bilingual empty state when Items.length === 0', () => {
    ordersRef.current.data = { Items: [], TotalCount: 0 };
    ordersRef.current.isLoading = false;
    wrap(<OrdersTable filter={{}} />);

    expect(screen.getByTestId('orders-empty')).toBeInTheDocument();
    expect(screen.getByText('Chưa có đơn hàng nào')).toBeInTheDocument();
  });

  it('handles a null CurrentSagaState by rendering the "Pending" placeholder', () => {
    ordersRef.current.data = {
      Items: [row({ Id: '01HABC0009', CurrentSagaState: null })],
      TotalCount: 1,
    };
    wrap(<OrdersTable filter={{}} />);

    expect(screen.getByText('Chưa khởi tạo')).toBeInTheDocument();
  });

  it('renders the Age cell with parsed TimeSpan output (Vietnamese)', () => {
    ordersRef.current.data = {
      Items: [row({ Id: '01HABC0010', Age: '01:23:45' })],
      TotalCount: 1,
    };
    wrap(<OrdersTable filter={{}} />);

    // "1 giờ 23 phút" — derived from "01:23:45".
    expect(screen.getByText('1 giờ 23 phút')).toBeInTheDocument();
  });

  it('renders "—" when LastTransitionAt is null', () => {
    ordersRef.current.data = {
      Items: [row({ Id: '01HABC0011', LastTransitionAt: null })],
      TotalCount: 1,
    };
    wrap(<OrdersTable filter={{}} />);

    // The age cell renders something parsed, but the last-transition cell
    // renders the literal em-dash placeholder.
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });
});
