/**
 * useOrderMutations — Sprint-7 plan U8.
 *
 * TanStack Query mutation handle for the dev-mode Orders seed endpoint:
 *   - `seedOrder` — POST /api/outbound/orders/seed (dev only; 404 outside Development)
 *
 * The mutation:
 *   1. Generates a fresh ULID for the `Idempotency-Key` header on every
 *      call. Audit-only dedupe (Sprint-6 trade-off #2 carries forward);
 *      retries get a new ULID by design.
 *   2. On success: invalidates the broad `['orders']` query key so list +
 *      KPI surfaces refetch.
 *   3. On success: pushes a success toast (4 s dwell) carrying the new
 *      order id.
 *   4. On error: pushes an error toast with the idempotency-key + trace-id
 *      extracted from the ApiError body (both camelCase `traceId` and
 *      PascalCase `TraceId` recognised — Sprint-6 trade-off #6).
 *   5. 404 (env-not-dev) is treated like any other ApiError; the toast
 *      title differentiates so operators in non-Dev environments see a
 *      clear message rather than a generic "failed" string.
 *
 * Mirrors `useInventoryMutations.ts` line-for-line on the toast + key
 * shape so the two surfaces stay reviewable together.
 */

import { useRef } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ordersApi, type SeedOrderRequest, type OrderResponse } from '../api/orders';
import { ApiError } from '../api/httpClient';
import { useToast } from './useToast';
import { t } from './useLocale';
import { ulid } from '../lib/ulid';

function extractTraceId(err: unknown): string | undefined {
  if (!(err instanceof ApiError)) return undefined;
  if (typeof err.body !== 'object' || err.body === null) return undefined;
  const b = err.body as Record<string, unknown>;
  if (typeof b.traceId === 'string') return b.traceId;
  if (typeof b.TraceId === 'string') return b.TraceId;
  return undefined;
}

function isEnvNotDevError(err: unknown): boolean {
  if (!(err instanceof ApiError)) return false;
  if (err.status !== 404) return false;
  if (typeof err.body !== 'object' || err.body === null) return false;
  const b = err.body as Record<string, unknown>;
  // Backend ProblemDetails for the seed endpoint sets code = 'environment_not_dev'.
  // Read both casings so the wire normalisation in Sprint-7 doesn't break us.
  const code =
    typeof b.code === 'string' ? b.code : typeof b.Code === 'string' ? b.Code : null;
  return code === 'environment_not_dev';
}

export function useSeedOrderMutation() {
  const qc = useQueryClient();
  const pushToast = useToast((s) => s.push);

  // Captures the key the last in-flight request sent so onError can show
  // it without needing access to the mutationFn's local scope. Forms only
  // submit one seed at a time — overwriting is safe.
  const lastKey = useRef('');

  // TVariables is `SeedOrderRequest | void` so call sites can opt into the
  // server's record-defaults shape with `mutateAsync()` — no args. TanStack
  // Query v5's conditional `MutateAsyncFunction` makes the variables param
  // optional whenever `void` is in the TVariables union.
  return useMutation<OrderResponse, unknown, SeedOrderRequest | void>({
    mutationFn: (input) => {
      const key = ulid();
      lastKey.current = key;
      return ordersApi.seed((input as SeedOrderRequest | undefined) ?? {}, {
        idempotencyKey: key,
      });
    },
    onSuccess: (order) => {
      qc.invalidateQueries({ queryKey: ['orders'] });
      pushToast({
        kind: 'success',
        title: t('Đã tạo đơn mẫu', 'Order seeded'),
        body: order.channelExternalOrderId,
      });
    },
    onError: (err: unknown) => {
      const isEnvBlocked = isEnvNotDevError(err);
      pushToast({
        kind: 'error',
        title: isEnvBlocked
          ? t(
              'Seed chỉ chạy trong môi trường Dev',
              'Seed only available in Development',
            )
          : t('Lỗi tạo đơn mẫu', 'Order seed failed'),
        idempotencyKey: lastKey.current,
        traceId: extractTraceId(err),
      });
    },
  });
}

/**
 * Aggregate hook so call sites can match the Sprint-6 `useInventoryMutations`
 * shape (one hook returns the bag of mutation handles). Keeps the import
 * surface symmetrical even though Sprint-7 ships a single mutation.
 */
export function useOrderMutations() {
  const seedOrder = useSeedOrderMutation();
  return { seedOrder };
}
