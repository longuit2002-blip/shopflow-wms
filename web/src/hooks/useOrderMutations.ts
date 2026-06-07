/**
 * useOrderMutations — Sprint-7 plan U8 + Sprint-11 U2 extensions.
 *
 * TanStack Query mutation handles for the Orders surface:
 *   - `seedOrder`       — POST /api/outbound/orders/seed (dev only)
 *   - `confirmPick`     — POST /api/outbound/orders/{id}/confirm-pick
 *                         (Sprint-11 U2 — Picker; gated by
 *                         `outbound.orders.pick-confirm` server-side)
 *   - `markPickFailed`  — POST /api/outbound/orders/{id}/mark-pick-failed
 *                         (Sprint-11 U2 — Picker; modal-captured reason)
 *
 * All three mutations share the same Sprint-7-baseline discipline:
 *   1. Fresh ULID for the `Idempotency-Key` header on every call. Audit-
 *      only dedupe (Sprint-6 trade-off #2 carries forward); retries
 *      always get a new ULID by design.
 *   2. On success: invalidate the configured query keys so list / KPI /
 *      detail / transitions surfaces refetch.
 *   3. On success: push a success toast (4 s dwell).
 *   4. On error: push an error toast with the idempotency-key + traceId
 *      extracted from the ApiError body (both camelCase + PascalCase
 *      recognised — Sprint-6 trade-off #6).
 *
 * The Sprint-11 refactor extracts `createIdempotentMutation` so the three
 * mutations stay one-liner consumers — keeps a future fourth (cancel?
 * confirm-ship?) at the same shape.
 */

import { useRef } from 'react';
import { useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { ordersApi, type SeedOrderRequest, type OrderResponse, type ConfirmShipResponse } from '../api/orders';
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

// ── Shared helper (Sprint-11 U2 refactor) ────────────────────────────────

/**
 * Toast labels for a mutation — varies per call site so each surface can
 * push its own bilingual copy. The shape stays consistent so call sites
 * never have to reach into the toast push themselves.
 */
export interface IdempotentMutationToasts<TRes> {
  /** Title for the success toast. */
  successTitle: string;
  /** Optional body (e.g. the new order id) — read from the response. */
  successBody?: (res: TRes) => string | undefined;
  /** Title for the generic error path. */
  errorTitle: string;
  /**
   * Optional override for a known-shape error — receives the unknown err
   * and returns a different title when the body matches a sentinel code.
   * Returning undefined falls back to `errorTitle`.
   */
  errorTitleFor?: (err: unknown) => string | undefined;
}

/**
 * Factor — builds a TanStack-Query useMutation handle that owns
 * idempotency-key generation, query-key invalidation, and toast push.
 *
 * The mutation `fn` is responsible for the underlying httpClient call;
 * the helper threads the ULID through as `idempotencyKey` on the second
 * arg. Each call gets a fresh ULID via the `useRef` "last key" so the
 * onError handler can surface it in the toast.
 *
 * @param fn               The fetch — receives variables + idempotency key.
 * @param invalidateKeys   Query keys to invalidate on success.
 * @param toasts           Bilingual labels for the success + error toasts.
 */
function createIdempotentMutation<TReq, TRes>(
  fn: (input: TReq, key: string) => Promise<TRes>,
  invalidateKeys: QueryKey[],
  toasts: IdempotentMutationToasts<TRes>,
) {
  // Returned function is intentionally a hook — call inside a component.
  return function useIdempotentMutation() {
    const qc = useQueryClient();
    const pushToast = useToast((s) => s.push);
    const lastKey = useRef('');

    return useMutation<TRes, unknown, TReq>({
      mutationFn: (input) => {
        const key = ulid();
        lastKey.current = key;
        return fn(input, key);
      },
      onSuccess: (res) => {
        for (const k of invalidateKeys) {
          qc.invalidateQueries({ queryKey: k });
        }
        pushToast({
          kind: 'success',
          title: toasts.successTitle,
          body: toasts.successBody?.(res),
        });
      },
      onError: (err: unknown) => {
        const title = toasts.errorTitleFor?.(err) ?? toasts.errorTitle;
        pushToast({
          kind: 'error',
          title,
          idempotencyKey: lastKey.current,
          traceId: extractTraceId(err),
        });
      },
    });
  };
}

// ── seedOrder (Sprint-7 U8 — preserved) ──────────────────────────────────

export const useSeedOrderMutation = createIdempotentMutation<
  SeedOrderRequest | void,
  OrderResponse
>(
  (input, key) =>
    ordersApi.seed((input as SeedOrderRequest | undefined) ?? {}, {
      idempotencyKey: key,
    }),
  [['orders']],
  {
    successTitle: t('Đã tạo đơn mẫu', 'Order seeded'),
    successBody: (order) => order.channelExternalOrderId,
    errorTitle: t('Lỗi tạo đơn mẫu', 'Order seed failed'),
    errorTitleFor: (err) =>
      isEnvNotDevError(err)
        ? t(
            'Seed chỉ chạy trong môi trường Dev',
            'Seed only available in Development',
          )
        : undefined,
  },
);

// ── confirmPick (Sprint-11 U2 — Picker) ──────────────────────────────────

/**
 * Variables shape for confirmPick — orderId is the route param the
 * detail-screen button passes in. Wrapped as a record so future per-call
 * fields (e.g. picker note) can land without breaking the call-site.
 */
export interface ConfirmPickVariables {
  orderId: string;
}

export const useConfirmPickMutation = createIdempotentMutation<
  ConfirmPickVariables | string,
  OrderResponse
>(
  (input, key) => {
    const orderId = typeof input === 'string' ? input : input.orderId;
    return ordersApi.confirmPick(orderId, { idempotencyKey: key });
  },
  [['orders'], ['order-detail'], ['order-transitions']],
  {
    successTitle: t('Xác nhận lấy hàng thành công', 'Pick confirmed'),
    errorTitle: t('Lỗi xác nhận lấy hàng', 'Pick confirm failed'),
  },
);

// ── markPickFailed (Sprint-11 U2 — Picker) ───────────────────────────────

/**
 * Variables shape for markPickFailed — orderId + reason. The reason is
 * captured by `MarkPickFailedModal` and is mandatory non-empty (modal
 * disables submit until populated).
 */
export interface MarkPickFailedVariables {
  orderId: string;
  reason: string;
}

export const useMarkPickFailedMutation = createIdempotentMutation<
  MarkPickFailedVariables,
  OrderResponse
>(
  (input, key) =>
    ordersApi.markPickFailed(input.orderId, input.reason, {
      idempotencyKey: key,
    }),
  [['orders'], ['order-detail'], ['order-transitions']],
  {
    successTitle: t('Đã báo lỗi lấy hàng', 'Pick failed reported'),
    errorTitle: t('Lỗi báo lỗi lấy hàng', 'Mark pick failed errored'),
  },
);

// ── confirmShip (Sprint-12 U2 — Dispatcher) ──────────────────────────────

/**
 * Variables shape for confirmShip — orderId is the route param the
 * detail-screen button passes in. The endpoint takes no request body
 * (carrier label is generated server-side); orderId is the only thing
 * the caller threads through. Wrapped as a record (or accepts a raw
 * string for the button call-site's `confirmShip.mutate(orderId)`
 * shorthand) so future per-call fields can land without breaking
 * existing consumers.
 */
export interface ConfirmShipVariables {
  orderId: string;
}

export const useConfirmShipMutation = createIdempotentMutation<
  ConfirmShipVariables | string,
  ConfirmShipResponse
>(
  (input, key) => {
    const orderId = typeof input === 'string' ? input : input.orderId;
    return ordersApi.confirmShip(orderId, { idempotencyKey: key });
  },
  [['orders'], ['order-detail'], ['order-transitions']],
  {
    successTitle: t('Xác nhận giao hàng thành công', 'Ship confirmed'),
    // Surface the carrier tracking number in the toast body so the
    // Dispatcher has a copyable handle. The persistent tracking-pill
    // on order-detail header (Sprint-12 KTD10) is the post-dismiss
    // fallback.
    successBody: (res) => res.trackingNumber,
    errorTitle: t('Lỗi xác nhận giao hàng', 'Ship confirm failed'),
  },
);

// ── Aggregator ───────────────────────────────────────────────────────────

/**
 * Aggregate hook so call sites can match the Sprint-6 `useInventoryMutations`
 * shape (one hook returns the bag of mutation handles).
 */
export function useOrderMutations() {
  const seedOrder = useSeedOrderMutation();
  const confirmPick = useConfirmPickMutation();
  const markPickFailed = useMarkPickFailedMutation();
  const confirmShip = useConfirmShipMutation();
  return { seedOrder, confirmPick, markPickFailed, confirmShip };
}
