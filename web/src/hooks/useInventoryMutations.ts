/**
 * useInventoryMutations — Sprint-6 plan U11/U12.
 *
 * TanStack Query mutation handles for the Inventory write surface:
 *   - `adjust`   — POST /api/v1/inventory/adjustments  (R8)
 *   - `setThreshold` — PUT /api/v1/inventory/skus/{sku}/threshold (R9)
 *   - `setFlashSale` — PUT /api/v1/inventory/skus/{sku}/flash-sale (R10, U12)
 *   - `createSku`    — POST /api/v1/inventory/skus     (R11, U12)
 *
 * Each mutation:
 *   1. Generates a fresh ULID for the `Idempotency-Key` header on every
 *      call. Sprint-6 trade-off #2 documents that server-side dedupe is
 *      driven by the audit table — so a per-call key is purely audit /
 *      observability, and "retry → new ULID" is the intended shape (plan
 *      U11 test scenarios line ~844).
 *   2. On success: invalidates the three Inventory query keys
 *      (`skus`, `summary`, `ledger`) so the table, KPI strip, and drawer
 *      all refetch within their normal poll/refresh windows.
 *   3. On success: pushes a success toast (4 s dwell).
 *   4. On error: pushes an error toast carrying the idempotency-key + the
 *      trace-id extracted from the ApiError body (both camelCase
 *      `traceId` and PascalCase `TraceId` are recognised — Sprint-6
 *      trade-off #6 keeps the wire shape PascalCase).
 *
 * The mutation handles are exposed via `useMutation`'s full result object
 * so callers can read `isPending`, `error`, and call `reset()` when the
 * modal closes.
 */

import { useRef } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { inventoryApi } from '../api/inventory';
import { ApiError } from '../api/httpClient';
import { useToast } from './useToast';
import { t } from './useLocale';
import { ulid } from '../lib/ulid';

export interface AdjustInput {
  sku: string;
  delta: number;
  reason: string;
  note?: string;
}

export interface SetThresholdInput {
  sku: string;
  threshold: number;
}

export interface SetFlashSaleInput {
  sku: string;
  active: boolean;
}

export interface CreateSkuInput {
  sku: string;
  initialAvailable: number;
}

function extractTraceId(err: unknown): string | undefined {
  if (!(err instanceof ApiError)) return undefined;
  if (typeof err.body !== 'object' || err.body === null) return undefined;
  const b = err.body as Record<string, unknown>;
  if (typeof b.traceId === 'string') return b.traceId;
  if (typeof b.TraceId === 'string') return b.TraceId;
  return undefined;
}

function signedDelta(n: number): string {
  return n >= 0 ? `+${n}` : `${n}`;
}

export function useInventoryMutations() {
  const qc = useQueryClient();
  const pushToast = useToast((s) => s.push);

  // Capture the key sent for the last in-flight request per mutation so
  // the onError handler can surface it without needing access to the
  // mutationFn's local scope. Forms only submit one mutation at a time so
  // overwriting is safe — there is no concurrent-request scenario in U11.
  const lastAdjustKey = useRef('');
  const lastThresholdKey = useRef('');
  const lastFlashSaleKey = useRef('');
  const lastCreateKey = useRef('');

  function invalidateInventoryQueries(): void {
    qc.invalidateQueries({ queryKey: ['inventory', 'skus'] });
    qc.invalidateQueries({ queryKey: ['inventory', 'summary'] });
    qc.invalidateQueries({ queryKey: ['inventory', 'ledger'] });
  }

  const adjust = useMutation({
    mutationFn: (input: AdjustInput) => {
      const key = ulid();
      lastAdjustKey.current = key;
      return inventoryApi.adjust(input, { idempotencyKey: key });
    },
    onSuccess: (_data, vars) => {
      invalidateInventoryQueries();
      pushToast({
        kind: 'success',
        title: t('Đã điều chỉnh tồn', 'Stock adjusted'),
        body: `${vars.sku}: ${signedDelta(vars.delta)}`,
      });
    },
    onError: (err: unknown) => {
      pushToast({
        kind: 'error',
        title: t('Lỗi điều chỉnh tồn', 'Stock adjustment failed'),
        idempotencyKey: lastAdjustKey.current,
        traceId: extractTraceId(err),
      });
    },
  });

  const setThreshold = useMutation({
    mutationFn: (input: SetThresholdInput) => {
      const key = ulid();
      lastThresholdKey.current = key;
      return inventoryApi.setThreshold(input.sku, input.threshold, {
        idempotencyKey: key,
      });
    },
    onSuccess: (_data, vars) => {
      invalidateInventoryQueries();
      pushToast({
        kind: 'success',
        title: t('Đã lưu mức an toàn', 'Threshold saved'),
        body: `${vars.sku}: ${vars.threshold}`,
      });
    },
    onError: (err: unknown) => {
      pushToast({
        kind: 'error',
        title: t('Lỗi lưu mức an toàn', 'Threshold save failed'),
        idempotencyKey: lastThresholdKey.current,
        traceId: extractTraceId(err),
      });
    },
  });

  const setFlashSale = useMutation({
    mutationFn: (input: SetFlashSaleInput) => {
      const key = ulid();
      lastFlashSaleKey.current = key;
      return inventoryApi.setFlashSale(input.sku, input.active, {
        idempotencyKey: key,
      });
    },
    onSuccess: (_data, vars) => {
      invalidateInventoryQueries();
      pushToast({
        kind: 'success',
        title: vars.active
          ? t('Bật flash-sale', 'Flash-sale enabled')
          : t('Tắt flash-sale', 'Flash-sale disabled'),
        body: vars.sku,
      });
    },
    onError: (err: unknown) => {
      pushToast({
        kind: 'error',
        title: t('Lỗi bật/tắt flash-sale', 'Flash-sale toggle failed'),
        idempotencyKey: lastFlashSaleKey.current,
        traceId: extractTraceId(err),
      });
    },
  });

  const createSku = useMutation({
    mutationFn: (input: CreateSkuInput) => {
      const key = ulid();
      lastCreateKey.current = key;
      return inventoryApi.create(input, { idempotencyKey: key });
    },
    onSuccess: (_data, vars) => {
      invalidateInventoryQueries();
      pushToast({
        kind: 'success',
        title: t('Đã tạo SKU', 'SKU created'),
        body: vars.sku,
      });
    },
    onError: (err: unknown) => {
      pushToast({
        kind: 'error',
        title: t('Lỗi tạo SKU', 'SKU creation failed'),
        idempotencyKey: lastCreateKey.current,
        traceId: extractTraceId(err),
      });
    },
  });

  return { adjust, setThreshold, setFlashSale, createSku };
}
