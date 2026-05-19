/**
 * TanStack Query client singleton — Sprint-6 plan U9.
 *
 * Sensible defaults for the Inventory polling loop:
 *   - 2-second refetchInterval on inventory queries (set per-query).
 *   - refetchIntervalInBackground: false — pause when the tab is
 *     inactive so we don't burn battery + bandwidth.
 *   - retry: 1 — let transient flakes hit the user once with a stale
 *     view rather than wedging into a retry storm.
 *   - staleTime: 0 — every refetch returns fresh; per-query overrides
 *     can extend.
 */

import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 0,
      refetchIntervalInBackground: false,
    },
  },
});
