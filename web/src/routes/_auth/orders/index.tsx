import { useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { OrdersKpiStrip } from '../../../components/orders/OrdersKpiStrip';
import { OrdersFilterStrip } from '../../../components/orders/OrdersFilterStrip';
import { OrdersTable } from '../../../components/orders/OrdersTable';
import { SeedTestOrderButton } from '../../../components/orders/SeedTestOrderButton';
import type { OrdersFilter } from '../../../api/orders';
import { t, useLocale } from '../../../hooks/useLocale';

/**
 * Orders list route — Sprint-7 plan U10.
 *
 * Replaces the Sprint-6 `/outbound` ComingSoon stub. Assembles:
 *   KpiStrip + FilterStrip + OrdersTable + (DEV-only) SeedTestOrderButton.
 *
 * Filter state is local React state (Sprint-6 trade-off #4 carries forward
 * — no URL-search-params persistence). The table re-runs the underlying
 * TanStack Query as soon as `filter` changes.
 *
 * Auth guard inherited from the parent `_auth.tsx` layout — unauthenticated
 * users bounce to /login before this route mounts.
 *
 * The detail route at `/orders/$orderId` lands in Sprint-7 U13; this route
 * navigates to it via the in-table button click.
 */
export const Route = createFileRoute('/_auth/orders/')({
  component: OrdersListRouteComponent,
});

function OrdersListRouteComponent() {
  useLocale();
  const [filter, setFilter] = useState<OrdersFilter>({});

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
      <OrdersFilterStrip value={filter} onChange={setFilter} />
      <OrdersTable filter={filter} />
    </div>
  );
}
