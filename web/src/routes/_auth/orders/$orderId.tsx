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
 *     placeholder when allocations=[]).
 *
 * Failure cause:
 *   - inferFailureCause(transitions) walks the audit log backwards and
 *     returns the eventType of the last `→ CompensatingReservation`
 *     transition. Null when the saga did not compensate.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { createFileRoute, Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { useOrderDetailQuery, useOrderTransitionsQuery } from '../../../hooks/useOrdersQuery';
import { useOrderMutations } from '../../../hooks/useOrderMutations';
import { usePerm } from '../../../hooks/usePerm';
import { SagaPipeline } from '../../../components/orders/SagaPipeline';
import { OrderLineItems } from '../../../components/orders/OrderLineItems';
import { TransitionsLog } from '../../../components/orders/TransitionsLog';
import { MarkPickFailedModal } from '../../../components/orders/MarkPickFailedModal';
import { LedgerDrawer } from '../../../components/inventory/LedgerDrawer';
import { Pill, type PillKind } from '../../../components/primitives/Pill';
import { t, useLocale } from '../../../hooks/useLocale';
import type { OrderTransitionDto, OrderLineResponse } from '../../../api/orders';
import type { SkuListItem } from '../../../api/inventory';

export const Route = createFileRoute('/_auth/orders/$orderId')({
  component: OrderDetailRouteComponent,
});

/**
 * Walks the transitions backwards and returns the `eventType` of the
 * last `→ CompensatingReservation` row. Null when no compensation path
 * has been entered.
 */
export function inferFailureCause(transitions: OrderTransitionDto[]): string | null {
  for (let i = transitions.length - 1; i >= 0; i--) {
    if (transitions[i]!.toState === 'CompensatingReservation') {
      return transitions[i]!.eventType;
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

export function OrderDetailRouteComponent() {
  useLocale();
  const { orderId } = Route.useParams();

  const { data: detail, isLoading: detailLoading, error: detailErr } = useOrderDetailQuery(orderId);
  const { data: transitions = [] } = useOrderTransitionsQuery(orderId);

  const [openLedgerSku, setOpenLedgerSku] = useState<string | null>(null);

  // Sprint-11 U2 — Picker action wiring. `usePerm` (reactive — KTD3)
  // unmounts the buttons mid-session if the operator's perm[] narrows.
  // Server-side [Authorize(Policy="outbound.orders.pick-confirm")]
  // remains authoritative.
  const canPickConfirm = usePerm('outbound.orders.pick-confirm');
  // Sprint-12 U3 — Dispatcher action wiring. KTD2 — gate on
  // `detail.status` (Order aggregate field, which DOES reach
  // 'AwaitingShip') not `detail.currentSagaState` (saga's CurrentState,
  // which never enters 'AwaitingShip' on the happy path —
  // FulfillmentSaga.cs:213 TODO documents the missing auto-transition).
  // Server-side [Authorize(Policy="outbound.orders.ship-confirm")]
  // remains authoritative; `usePerm` (reactive) re-renders if the
  // operator's perm[] narrows mid-session.
  const canShipConfirm = usePerm('outbound.orders.ship-confirm');
  const { confirmPick, markPickFailed, confirmShip } = useOrderMutations();
  const [markFailedOpen, setMarkFailedOpen] = useState(false);
  // Optimistic-hide via local state: once a successful confirm-pick fires
  // we suppress the buttons until detail.currentSagaState ticks past
  // AwaitingPick on the next refetch (DL-007).
  const [justConfirmed, setJustConfirmed] = useState(false);
  // Sprint-12 U3 — parallel optimistic-hide for ship-confirm. Cleared
  // when `detail.status` ticks past 'AwaitingShip' (i.e. reaches
  // 'Shipped').
  const [justShipped, setJustShipped] = useState(false);

  const failureCause = useMemo(() => inferFailureCause(transitions), [transitions]);

  // Clear the optimistic-hide once the server-side saga state has
  // actually moved past AwaitingPick. Re-entering AwaitingPick (rare —
  // would require a saga retry) then re-shows the buttons.
  useEffect(() => {
    if (detail?.currentSagaState && detail.currentSagaState !== 'AwaitingPick') {
      setJustConfirmed(false);
    }
  }, [detail?.currentSagaState]);

  // Sprint-12 U3 — parallel ship-confirm hide-clear. Mirrors the
  // pick-confirm pattern but reads `detail.status` (Order aggregate
  // field per KTD2) rather than `currentSagaState`.
  useEffect(() => {
    if (detail?.status && detail.status !== 'AwaitingShip') {
      setJustShipped(false);
    }
  }, [detail?.status]);

  // The drawer was built for the inventory shape — build a minimal stub
  // from the open SKU so the header renders. The internal useSkuLedgerQuery
  // is the source of truth for the ledger body itself.
  const drawerItem = useMemo<SkuListItem | null>(() => {
    if (openLedgerSku === null) return null;
    return {
      sku: openLedgerSku,
      available: 0,
      reserved: 0,
      name: null,
      category: null,
      threshold: null,
      isFlashSale: false,
      allocations: [],
      p24Outbound: 0,
    };
  }, [openLedgerSku]);

  const closeDrawer = useCallback(() => setOpenLedgerSku(null), []);
  const handleLineClick = useCallback((line: OrderLineResponse) => {
    setOpenLedgerSku(line.sku);
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
        <div className="t-lg mono" data-testid="order-detail-order-id" style={{ fontWeight: 600 }}>
          {detail.channelExternalOrderId}
        </div>
        {detail.currentSagaState && (
          <Pill
            kind={sagaStatePillKind(detail.currentSagaState)}
            data-testid="order-detail-saga-pill"
          >
            {detail.currentSagaState}
          </Pill>
        )}
        {detail.trackingNumber !== null && detail.labelUrl !== null && (
          <div
            data-testid="order-detail-tracking"
            style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--s-2)' }}
          >
            <span className="lbl" style={{ color: 'var(--ink-2)' }}>
              {t('Mã vận đơn', 'Tracking')}:
            </span>
            <Pill kind="ok" data-testid="order-detail-tracking-pill">
              {detail.trackingNumber}
            </Pill>
          </div>
        )}
        <div className="t-sm" data-testid="order-detail-channel" style={{ color: 'var(--ink-2)' }}>
          {t('Kênh', 'Channel')}: {detail.channel}
        </div>
      </header>

      <section data-testid="order-detail-saga-pipeline">
        <SagaPipeline
          currentState={detail.currentSagaState ?? 'AwaitingReservation'}
          transitions={transitions}
          failureCause={failureCause ?? undefined}
        />
      </section>

      {canPickConfirm && detail.currentSagaState === 'AwaitingPick' && !justConfirmed && (
        <section
          data-testid="order-detail-pick-actions"
          aria-label={t('Tác vụ Picker', 'Picker actions')}
          style={{
            display: 'flex',
            gap: 'var(--s-2)',
            flexWrap: 'wrap',
          }}
        >
          <button
            type="button"
            className="btn primary"
            data-testid="confirm-pick-button"
            disabled={confirmPick.isPending}
            aria-busy={confirmPick.isPending ? true : undefined}
            onClick={() =>
              confirmPick.mutate(orderId, {
                onSuccess: () => setJustConfirmed(true),
              })
            }
          >
            {confirmPick.isPending
              ? t('Đang xác nhận…', 'Confirming…')
              : t('Xác nhận lấy hàng', 'Confirm Pick')}
          </button>
          <button
            type="button"
            className="btn danger"
            data-testid="mark-pick-failed-button"
            disabled={markPickFailed.isPending}
            aria-busy={markPickFailed.isPending ? true : undefined}
            onClick={() => setMarkFailedOpen(true)}
          >
            {markPickFailed.isPending
              ? t('Đang gửi…', 'Submitting…')
              : t('Báo lỗi lấy hàng', 'Mark Pick Failed')}
          </button>
        </section>
      )}

      {canShipConfirm && detail.status === 'AwaitingShip' && !justShipped && (
        <section
          data-testid="order-detail-ship-actions"
          aria-label={t('Tác vụ Dispatcher', 'Dispatcher actions')}
          style={{
            display: 'flex',
            gap: 'var(--s-2)',
            flexWrap: 'wrap',
          }}
        >
          <button
            type="button"
            className="btn primary"
            data-testid="confirm-ship-button"
            disabled={confirmShip.isPending}
            aria-busy={confirmShip.isPending ? true : undefined}
            onClick={() =>
              confirmShip.mutate(orderId, {
                onSuccess: () => setJustShipped(true),
              })
            }
          >
            {confirmShip.isPending
              ? t('Đang xác nhận…', 'Confirming…')
              : t('Xác nhận giao hàng', 'Confirm Ship')}
          </button>
        </section>
      )}

      <section data-testid="order-detail-lines">
        <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
          {t('Dòng đơn', 'Line items')}
        </div>
        <OrderLineItems lines={detail.lines} onLineClick={handleLineClick} />
      </section>

      <section data-testid="order-detail-transitions">
        <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
          {t('Lịch sử chuyển trạng thái', 'Saga transition history')}
        </div>
        <TransitionsLog transitions={transitions} />
      </section>

      <LedgerDrawer item={drawerItem} onClose={closeDrawer} />

      <MarkPickFailedModal
        isOpen={markFailedOpen}
        onClose={() => setMarkFailedOpen(false)}
        isPending={markPickFailed.isPending}
        onSubmit={(reason) =>
          markPickFailed.mutate(
            { orderId, reason },
            {
              onSuccess: () => {
                setMarkFailedOpen(false);
                setJustConfirmed(true);
              },
            },
          )
        }
      />
    </div>
  );
}
