/**
 * TanStack Query wrappers for the Inventory endpoints. Sprint-6 plan U9.
 *
 * Both queries refetch every 2 seconds while the tab is active. Sprint-7
 * swaps refetchInterval for SignalR push events; the hook signatures
 * stay the same so consuming components don't change.
 */

import { useQuery } from '@tanstack/react-query';
import { inventoryApi, type ListSkusParams } from '../api/inventory';

const POLL_MS = 2000;

export function useInventoryQuery(params: ListSkusParams = {}) {
  return useQuery({
    queryKey: ['inventory', 'skus', params],
    queryFn: () => inventoryApi.listSkus(params),
    refetchInterval: POLL_MS,
  });
}

export function useInventorySummaryQuery() {
  return useQuery({
    queryKey: ['inventory', 'summary'],
    queryFn: () => inventoryApi.summary(),
    refetchInterval: POLL_MS,
  });
}

export function useSkuLedgerQuery(sku: string | null, limit = 100) {
  return useQuery({
    queryKey: ['inventory', 'ledger', sku, limit],
    queryFn: () => inventoryApi.ledger(sku!, limit),
    enabled: sku !== null,
    refetchInterval: POLL_MS,
  });
}
