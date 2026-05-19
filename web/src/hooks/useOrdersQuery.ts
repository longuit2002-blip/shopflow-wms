/**
 * TanStack Query wrappers for the Orders endpoints. Sprint-7 plan U8.
 *
 * Mirrors the Sprint-7 U9 inventory pattern (see `useInventoryQuery.ts`):
 * SignalR-driven invalidation with a 2-second polling fallback when the
 * hub is not connected (R13). Hook signatures stay unchanged per Sprint-6
 * KTD5 so consumers (the U10/U13 routes) only need to import and call.
 *
 * Broad vs narrow invalidation:
 *   - List + KPI hooks invalidate the broad `['orders']` prefix so any
 *     `saga_transitioned` event sweeps both surfaces in one shot.
 *   - Detail + transitions hooks narrow-invalidate `['orders', orderId]`
 *     only when the payload's `OrderId` matches the hook's orderId. The
 *     handler closure captures the orderId at mount; multiple open detail
 *     caches do not churn on every transition.
 *
 * SignalR event payload contract (Outbound.Api hub):
 *   `saga_transitioned` payload: `{ OrderId: string, ... }` — at minimum
 *   the order id is present. Additional fields (FromState, ToState, etc.)
 *   are ignored here; the consuming components refetch via the invalidated
 *   query.
 */

import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ordersApi,
  type OrdersFilter,
  type OrderListResponse,
  type OrderKpiResponse,
  type OrderDetailDto,
  type OrderTransitionDto,
} from '../api/orders';
import { useSignalR } from './useSignalR';
import { useAuth } from './useAuth';

const POLL_MS = 2000;

/**
 * Resolves the dynamic `refetchInterval` value from the singleton hub
 * state. Pulled out so the list + KPI hooks share the exact same
 * polling-fallback policy.
 */
function pollIntervalFor(hubState: string): number | false {
  return hubState === 'connected' ? false : POLL_MS;
}

// ── Subscription helpers ─────────────────────────────────────────────────

/**
 * Internal helper — bootstraps the hub on first mount (when a JWT is
 * present) and subscribes to `saga_transitioned`. The handler invalidates
 * the broad `['orders']` query key so the list + KPI caches both refetch.
 *
 * Mirrors `useStockChangedSubscription` from `useInventoryQuery.ts`.
 */
function useSagaTransitionSubscription(): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (useAuth.getState().jwt) {
      void useSignalR.getState().connect();
    }

    const unsubscribe = useSignalR
      .getState()
      .subscribe('saga_transitioned', () => {
        queryClient.invalidateQueries({ queryKey: ['orders'] });
      });

    return unsubscribe;
  }, [queryClient]);
}

/**
 * Narrow variant for the detail + transitions hooks. The handler reads
 * the payload's `OrderId` field and only invalidates when it matches the
 * hook's orderId — so two open detail caches for different orders don't
 * churn against each other on every transition emit.
 *
 * The hub payload shape is treated defensively: if `OrderId` is absent or
 * malformed, the handler skips invalidation (the broad-subscription hook
 * still fires for the list, so list refetches are unaffected).
 */
function useSagaTransitionSubscriptionForOrder(orderId: string): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (useAuth.getState().jwt) {
      void useSignalR.getState().connect();
    }

    const unsubscribe = useSignalR
      .getState()
      .subscribe('saga_transitioned', (payload: unknown) => {
        if (!payload || typeof payload !== 'object') return;
        const p = payload as { OrderId?: unknown };
        if (typeof p.OrderId !== 'string') return;
        if (p.OrderId !== orderId) return;
        queryClient.invalidateQueries({ queryKey: ['orders', orderId] });
      });

    return unsubscribe;
  }, [queryClient, orderId]);
}

// ── Query hooks ──────────────────────────────────────────────────────────

/**
 * List query — `GET /api/outbound/orders`. Polls every 2 s when the hub
 * is not connected; SignalR `saga_transitioned` event drives invalidation
 * otherwise (R13 fallback policy).
 */
export function useOrdersListQuery(filter: OrdersFilter = {}) {
  useSagaTransitionSubscription();
  const hubState = useSignalR((s) => s.state);
  return useQuery<OrderListResponse>({
    queryKey: ['orders', 'list', filter],
    queryFn: () => ordersApi.list(filter),
    refetchInterval: pollIntervalFor(hubState),
  });
}

/**
 * KPI query — `GET /api/outbound/orders/kpis`. Same polling + SignalR
 * pattern as the list hook.
 */
export function useOrderKpiQuery() {
  useSagaTransitionSubscription();
  const hubState = useSignalR((s) => s.state);
  return useQuery<OrderKpiResponse>({
    queryKey: ['orders', 'kpi'],
    queryFn: () => ordersApi.kpis(),
    refetchInterval: pollIntervalFor(hubState),
  });
}

/**
 * Detail query — `GET /api/outbound/orders/{id}`. SignalR-only refresh
 * (no polling fallback) since the detail panel opens on demand. Narrow-
 * invalidates `['orders', orderId]` only for transitions on this order.
 */
export function useOrderDetailQuery(orderId: string) {
  useSagaTransitionSubscriptionForOrder(orderId);
  return useQuery<OrderDetailDto>({
    queryKey: ['orders', orderId, 'detail'],
    queryFn: () => ordersApi.detail(orderId),
    enabled: orderId !== '',
    refetchInterval: false,
  });
}

/**
 * Transitions query — `GET /api/outbound/orders/{id}/transitions`. Same
 * SignalR-only refresh as the detail hook; the timeline panel reads this
 * cache and the broad-narrow-invalidate split keeps unrelated orders' caches
 * quiet.
 */
export function useOrderTransitionsQuery(orderId: string) {
  useSagaTransitionSubscriptionForOrder(orderId);
  return useQuery<OrderTransitionDto[]>({
    queryKey: ['orders', orderId, 'transitions'],
    queryFn: () => ordersApi.transitions(orderId),
    enabled: orderId !== '',
    refetchInterval: false,
  });
}
