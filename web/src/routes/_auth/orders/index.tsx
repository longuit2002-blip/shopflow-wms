import { useCallback, useMemo } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { OrdersKpiStrip } from '../../../components/orders/OrdersKpiStrip';
import { OrdersFilterStrip } from '../../../components/orders/OrdersFilterStrip';
import { OrdersTable } from '../../../components/orders/OrdersTable';
import { SeedTestOrderButton } from '../../../components/orders/SeedTestOrderButton';
import { useFilterSearchParams } from '../../../hooks/useFilterSearchParams';
import type { OrdersFilter } from '../../../api/orders';
import { t, useLocale } from '../../../hooks/useLocale';

/**
 * Orders list route — Sprint-7 plan U10; Sprint-7.5 U7 URL-state migration.
 *
 * URL-search-params shape (per KTD5 / `useFilterSearchParams` adoption):
 *   ?status=Reserved|AwaitingPick|…|Cancelled
 *   ?channel=SHOPEE|LAZADA|TIKTOK|DIRECT
 *   ?since=ISO  / ?until=ISO    date-range (UTC, hh:mm:ss boundary)
 *   ?search=…                   free-text channel-external-order-id filter
 *   ?sort=createdAt|status      sort column (currently `createdAt` server-
 *                               side; preserved here for forward-compat)
 *   ?sortDir=asc|desc
 *   ?page=2,3,…                 pagination offset (1-based; 1 omitted)
 *
 * No `selected` / `ledger` keys — orders list navigates to a separate
 * detail route at `/orders/$orderId` instead of opening an in-place drawer.
 *
 * Filter/sort change auto-resets page (D-006 rule, enforced by the helper).
 * Reload preserves all state; deep-links open the same view; back button
 * traverses each filter change (every navigate is replace:false).
 *
 * Auth guard inherited from the parent `_auth.tsx` layout — unauthenticated
 * users bounce to /login before this route mounts.
 */
export const Route = createFileRoute('/_auth/orders/')({
  validateSearch: (raw: Record<string, unknown>): OrdersSearch => {
    return {
      status:
        typeof raw.status === 'string' && raw.status.length > 0 ? raw.status : undefined,
      channel:
        typeof raw.channel === 'string' && raw.channel.length > 0
          ? raw.channel
          : undefined,
      since:
        typeof raw.since === 'string' && raw.since.length > 0 ? raw.since : undefined,
      until:
        typeof raw.until === 'string' && raw.until.length > 0 ? raw.until : undefined,
      search:
        typeof raw.search === 'string' && raw.search.length > 0 ? raw.search : undefined,
      sort:
        typeof raw.sort === 'string' && raw.sort.length > 0 ? raw.sort : undefined,
      sortDir: isSortDir(raw.sortDir) ? raw.sortDir : undefined,
      page: toPositiveInt(raw.page) ?? undefined,
    };
  },
  component: OrdersListRouteComponent,
});

// ── URL schema ───────────────────────────────────────────────────────────

export interface OrdersSearch extends Record<string, unknown> {
  status?: string;
  channel?: string;
  since?: string;
  until?: string;
  search?: string;
  sort?: string;
  sortDir?: 'asc' | 'desc';
  page?: number;
}

const ORDERS_DEFAULTS: OrdersSearch = {
  status: undefined,
  channel: undefined,
  since: undefined,
  until: undefined,
  search: undefined,
  sort: undefined,
  sortDir: undefined,
  page: undefined,
};

function isSortDir(v: unknown): v is 'asc' | 'desc' {
  return v === 'asc' || v === 'desc';
}

function toPositiveInt(v: unknown): number | null {
  if (typeof v === 'number' && Number.isInteger(v) && v >= 1) return v;
  if (typeof v === 'string' && /^\d+$/.test(v)) {
    const n = Number(v);
    if (n >= 1) return n;
  }
  return null;
}

// ── Component ────────────────────────────────────────────────────────────

const PAGE_SIZE = 50;

function OrdersListRouteComponent() {
  useLocale();

  const [search, setSearch] = useFilterSearchParams<OrdersSearch>(ORDERS_DEFAULTS, {
    from: '/_auth/orders/',
    // Changing any filter, the search box, or the sort resets page.
    resetOn: ['status', 'channel', 'since', 'until', 'search', 'sort', 'sortDir'],
    pageKey: 'page',
  });

  // The OrdersFilterStrip's existing API takes an OrdersFilter shape — we
  // adapt the URL-state to that shape, and adapt its onChange callbacks
  // back into setSearch patches.
  const filterValue = useMemo<OrdersFilter>(
    () => ({
      status: search.status,
      channel: search.channel,
      since: search.since,
      until: search.until,
      search: search.search,
    }),
    [search.status, search.channel, search.since, search.until, search.search],
  );

  const handleFilterChange = useCallback(
    (next: OrdersFilter) => {
      // Compute the patch — each field flips from its current value to
      // either the new value or `undefined`. The helper enforces the
      // page-reset rule.
      setSearch({
        status: next.status,
        channel: next.channel,
        since: next.since,
        until: next.until,
        search: next.search,
      });
    },
    [setSearch],
  );

  // Translate the URL state to the `useOrdersListQuery` filter shape. Page
  // becomes `skip = (page-1) * PAGE_SIZE`.
  const queryFilter = useMemo<OrdersFilter>(() => {
    const page = search.page ?? 1;
    return {
      ...filterValue,
      take: PAGE_SIZE,
      skip: (page - 1) * PAGE_SIZE,
    };
  }, [filterValue, search.page]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      <header
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--s-3)',
          padding: 'var(--s-4) var(--s-6)',
          borderBottom: '1px solid var(--line)',
        }}
      >
        <div style={{ flex: 1 }}>
          <h1 style={{ fontSize: 'var(--text-xl)', fontWeight: 600, margin: 0 }}>
            {t('Đơn hàng', 'Orders')}
          </h1>
          <p
            className="t-sm"
            style={{ margin: 0, marginTop: 'var(--s-1)', color: 'var(--ink-3)' }}
          >
            {t(
              'Saga fulfillment + tracking — đẩy real-time qua SignalR.',
              'Saga fulfillment + tracking — pushed in real time over SignalR.',
            )}
          </p>
        </div>
        <SeedTestOrderButton />
      </header>

      <OrdersKpiStrip />
      <OrdersFilterStrip value={filterValue} onChange={handleFilterChange} />
      <OrdersTable
        filter={queryFilter}
        page={search.page ?? 1}
        pageSize={PAGE_SIZE}
        onPageChange={(next) => setSearch({ page: next === 1 ? undefined : next })}
      />
    </div>
  );
}
