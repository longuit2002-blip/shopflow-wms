/**
 * Orders detail route — Sprint-7 plan U13.
 *
 * URL: /orders/$orderId. Composes:
 *   - Header (back link, order id, current saga state pill, channel)
 *   - <SagaPipeline>     (U11) — horizontal 8-node pipeline
 *   - <OrderLineItems>   (U13) — table of order lines + per-line "View
 *                                ledger" CTA (KTD11 cell-level button)
 *   - <TransitionsLog>   (U12) — append-only saga audit feed
 *   - <LedgerDrawer>     (Sprint-6) — opens when a line CTA is clicked
 *
 * Data:
 *   - useOrderDetailQuery + useOrderTransitionsQuery (U8). Both queries
 *     refresh via the SignalR `saga_transitioned` event filtered to this
 *     orderId; no polling fallback at the detail level.
 *
 * State:
 *   - openLedgerSku — Sprint-6 pattern; the LedgerDrawer reads this and
 *     its internal useSkuLedgerQuery handles the loading/empty/error
 *     states for the ledger body. A minimal SkuListItem stub is built
 *     from the line's SKU because the drawer expects the inventory shape
 *     for its header (allocation bar renders the trade-off #3 empty
 *     placeholder when Allocations=[]).
 *
 * Failure cause:
 *   - inferFailureCause(transitions) walks the audit log backwards and
 *     returns the EventType of the last `→ CompensatingReservation`
 *     transition. Null when the saga did not compensate.
 */

import { useCallback, useMemo, useState } from 'react';
import { createFileRoute, Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { useOrderDetailQuery, useOrderTransitionsQuery } from '../../../hooks/useOrdersQuery';
import { SagaPipeline } from '../../../components/orders/SagaPipeline';
import { OrderLineItems } from '../../../components/orders/OrderLineItems';
import { TransitionsLog } from '../../../components/orders/TransitionsLog';
import { LedgerDrawer } from '../../../components/inventory/LedgerDrawer';
import { Pill, type PillKind } from '../../../components/primitives/Pill';
import { t, useLocale } from '../../../hooks/useLocale';
import type { OrderTransitionDto, OrderLineResponse } from '../../../api/orders';
import type { SkuListItem } from '../../../api/inventory';

export const Route = createFileRoute('/_auth/orders/$orderId')({
  component: OrderDetailRouteComponent,
});

/**
 * Walks the transitions backwards and returns the `EventType` of the
 * last `→ CompensatingReservation` row. Null when no compensation path
 * has been entered.
 */
export function inferFailureCause(transitions: OrderTransitionDto[]): string | null {
  for (let i = transitions.length - 1; i >= 0; i--) {
    if (transitions[i].ToState === 'CompensatingReservation') {
      return transitions[i].EventType;
    }
  }
  return null;
}

/**
 * Resolves the appropriate <Pill> kind for the order's current saga state.
 * Cancelled / CompensatingReservation → `bad`; Shipped → `ok`; AwaitingX
 * → `warn`; everything else → `info`.
 */
function sagaStatePillKind(state: string | null): PillKind {
  if (state === null) return 'default';
  if (state === 'Cancelled' || state === 'CompensatingReservation') return 'bad';
  if (state === 'Shipped') return 'ok';
  if (state.startsWith('Awaiting')) return 'warn';
  return 'info';
}

function OrderDetailRouteComponent() {
  useLocale();
  const { orderId } = Route.useParams();

  const { data: detail, isLoading: detailLoading, error: detailErr } =
    useOrderDetailQuery(orderId);
  const { data: transitions = [] } = useOrderTransitionsQuery(orderId);

  const [openLedgerSku, setOpenLedgerSku] = useState<string | null>(null);

  const failureCause = useMemo(() => inferFailureCause(transitions), [transitions]);

  // The drawer was built for the inventory shape — build a minimal stub
  // from the open SKU so the header renders. The internal useSkuLedgerQuery
  // is the source of truth for the ledger body itself.
  const drawerItem = useMemo<SkuListItem | null>(() => {
    if (openLedgerSku === null) return null;
    return {
      Sku: openLedgerSku,
      Available: 0,
      Reserved: 0,
      Name: null,
      Category: null,
      Threshold: null,
      IsFlashSale: false,
      Allocations: [],
      P24Outbound: 0,
    };
  }, [openLedgerSku]);

  const closeDrawer = useCallback(() => setOpenLedgerSku(null), []);
  const handleLineClick = useCallback((line: OrderLineResponse) => {
    setOpenLedgerSku(line.Sku);
  }, []);

  if (detailLoading) {
    return (
      <div
        data-testid="order-detail-loading"
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 'var(--s-8)',
          color: 'var(--ink-2)',
        }}
      >
        {t('Đang tải đơn…', 'Loading order…')}
      </div>
    );
  }

  if (detailErr || !detail) {
    return (
      <div
        role="alert"
        data-testid="order-detail-not-found"
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 'var(--s-8)',
          gap: 'var(--s-3)',
          color: 'var(--bad-ink)',
        }}
      >
        <div className="t-lg" style={{ fontWeight: 600 }}>
          {t('Không tìm thấy đơn', 'Order not found')}
        </div>
        <Link to="/orders" className="btn" data-testid="order-detail-back-link-error">
          {t('Quay lại danh sách', 'Back to orders')}
        </Link>
      </div>
    );
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        flex: 1,
        minHeight: 0,
        gap: 'var(--s-5)',
        padding: 'var(--s-5)',
      }}
    >
      <header
        data-testid="order-detail-header"
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--s-3)',
          flexWrap: 'wrap',
        }}
      >
        <Link
          to="/orders"
          className="btn"
          data-testid="order-detail-back-link"
          aria-label={t('Quay lại danh sách đơn', 'Back to orders list')}
          style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--s-2)' }}
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t('Quay lại', 'Back')}
        </Link>
        <div
          className="t-lg mono"
          data-testid="order-detail-order-id"
          style={{ fontWeight: 600 }}
        >
          {detail.ChannelExternalOrderId}
        </div>
        {detail.CurrentSagaState && (
          <Pill
            kind={sagaStatePillKind(detail.CurrentSagaState)}
            data-testid="order-detail-saga-pill"
          >
            {detail.CurrentSagaState}
          </Pill>
        )}
        <div
          className="t-sm"
          data-testid="order-detail-channel"
          style={{ color: 'var(--ink-2)' }}
        >
          {t('Kênh', 'Channel')}: {detail.Channel}
        </div>
      </header>

      <section data-testid="order-detail-saga-pipeline">
        <SagaPipeline
          currentState={detail.CurrentSagaState ?? 'AwaitingReservation'}
          transitions={transitions}
          failureCause={failureCause ?? undefined}
        />
      </section>

      <section data-testid="order-detail-lines">
        <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
          {t('Dòng đơn', 'Line items')}
        </div>
        <OrderLineItems lines={detail.Lines} onLineClick={handleLineClick} />
      </section>

      <section data-testid="order-detail-transitions">
        <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
          {t('Lịch sử chuyển trạng thái', 'Saga transition history')}
        </div>
        <TransitionsLog transitions={transitions} />
      </section>

      <LedgerDrawer item={drawerItem} onClose={closeDrawer} />
    </div>
  );
}
