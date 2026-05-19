/**
 * TanStack Query wrappers for the Inventory endpoints. Sprint-6 plan U9.
 *
 * Sprint-7 plan U9 layers SignalR-driven invalidation on top of the
 * Sprint-6 polling base:
 *
 *   - `useStockChangedSubscription()` (internal helper) mounts a
 *     `stock_changed` hub subscription on the singleton `useSignalR` store
 *     and invalidates `['inventory']` (broad prefix-match) whenever an
 *     event arrives. The same helper also triggers `connect()` once a JWT
 *     is present so the connection bootstrap stays co-located with the
 *     first consumer (Sprint-7 has no other call site yet — see the U7
 *     hook for the no-op guard against double-connect from React strict
 *     mode dev re-mounts).
 *
 *   - `refetchInterval` becomes dynamic — when the hub state is
 *     `'connected'`, polling halts (`false`) because SignalR drives
 *     invalidation; in every other state (`idle | connecting |
 *     reconnecting | disconnected`) the Sprint-6 2-second poll resumes
 *     per R13.
 *
 * Hook signatures stay unchanged per Sprint-6 KTD5 so SkuTable,
 * InventoryKpiStrip, LedgerDrawer, etc. require zero call-site edits.
 */

import { useEffect } from 'react';
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { inventoryApi, type ListSkusParams } from '../api/inventory';
import { useSignalR } from './useSignalR';
import { useAuth } from './useAuth';

const POLL_MS = 2000;

/**
 * Resolves the dynamic `refetchInterval` value from the singleton hub
 * state. Pulled out so all three query hooks share the exact same
 * polling-fallback policy.
 */
function pollIntervalFor(hubState: string): number | false {
  return hubState === 'connected' ? false : POLL_MS;
}

/**
 * Internal helper — subscribes the current query client to `stock_changed`
 * hub events and triggers the hub connect bootstrap when a JWT is
 * present. Not exported; consumed only by the three inventory query
 * hooks in this module.
 *
 * The handler invalidates the broad `['inventory']` query key so every
 * inventory-rooted cache entry (skus / summary / ledger) refetches on
 * the next observer tick. TanStack Query treats the key as a prefix, so
 * a single broad invalidation covers the entire inventory surface.
 */
function useStockChangedSubscription(): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    // Bootstrap the hub connection on the first mount that has a JWT.
    // `connect()` itself is a no-op when the store is already connecting,
    // connected, or reconnecting, so accidental double-mount in dev
    // strict-mode does not kick off a second negotiate.
    if (useAuth.getState().jwt) {
      void useSignalR.getState().connect();
    }

    const unsubscribe = useSignalR
      .getState()
      .subscribe('stock_changed', () => {
        queryClient.invalidateQueries({ queryKey: ['inventory'] });
      });

    return unsubscribe;
  }, [queryClient]);
}

export function useInventoryQuery(params: ListSkusParams = {}) {
  useStockChangedSubscription();
  const hubState = useSignalR((s) => s.state);
  return useQuery({
    queryKey: ['inventory', 'skus', params],
    queryFn: () => inventoryApi.listSkus(params),
    refetchInterval: pollIntervalFor(hubState),
  });
}

export function useInventorySummaryQuery() {
  useStockChangedSubscription();
  const hubState = useSignalR((s) => s.state);
  return useQuery({
    queryKey: ['inventory', 'summary'],
    queryFn: () => inventoryApi.summary(),
    refetchInterval: pollIntervalFor(hubState),
  });
}

/**
 * Ledger fetcher for the reservation drawer. Sprint-7.5 U6 switched to
 * `useInfiniteQuery` for cursor pagination — drawer renders an explicit
 * "Load more" button (no infinite scroll) per origin R14. Default page
 * size 50; backend clamps to [1, 200].
 *
 * Intentionally NOT polled even when the hub is disconnected — the
 * drawer opens on demand, fetches once per cursor advance, and is
 * invalidated by adjust/threshold mutations (TanStack Query key match).
 * Sprint-7 U9 still wires the SignalR subscription so a `stock_changed`
 * event for the displayed SKU triggers a refetch via the broad
 * `['inventory']` invalidation.
 */
export function useSkuLedgerQuery(sku: string | null, limit = 50) {
  useStockChangedSubscription();
  return useInfiniteQuery({
    queryKey: ['inventory', 'ledger', sku, limit],
    queryFn: ({ pageParam }) =>
      inventoryApi.ledger(sku!, limit, pageParam as string | null),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? null,
    enabled: sku !== null,
    refetchInterval: false,
  });
}
