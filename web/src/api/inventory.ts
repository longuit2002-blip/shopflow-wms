/**
 * Typed Inventory API client — Sprint-6 U7/U8 backend contract.
 *
 * Consumed across U9-U12. All requests go through `httpClient` so
 * Authorization + X-Tenant-Slug + (for mutations) Idempotency-Key are
 * attached automatically.
 *
 * Wire shape (Sprint-7.5 U1+U2):
 *   - Backend now serialises with `JsonNamingPolicy.CamelCase` on both the
 *     MVC pipeline (`AddShopFlowControllers`) and the SignalR hub
 *     (`AddJsonProtocol`). Closes Sprint-6 trade-off #6. Frontend types
 *     mirror the new camelCase shape verbatim; PropertyNameCaseInsensitive
 *     is still on for round-trip safety, so request bodies are also sent
 *     camelCase.
 */

import { httpClient } from './httpClient';

export interface ChannelAllocation {
  channel: string;
  allocated: number;
}

export interface SkuListItem {
  sku: string;
  available: number;
  reserved: number;
  name: string | null;
  category: string | null;
  threshold: number | null;
  isFlashSale: boolean;
  allocations: ChannelAllocation[];
  p24Outbound: number;
}

export interface PaginatedSkuList {
  items: SkuListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface SkuLedgerEntry {
  id: string;
  orderId: string;
  orderLineId: string;
  status: string;
  quantity: number;
  timestamp: string;
  runningBalance: number;
}

export interface SkuLedger {
  items: SkuLedgerEntry[];
  nextCursor: string | null;
}

export interface InventorySummary {
  totalSkus: number;
  totalAvailable: number;
  totalReserved: number;
  belowThresholdCount: number;
  oversellRiskCount: number;
}

export interface UpdateSkuDimensionsPayload {
  length: number;
  width: number;
  height: number;
  unit: string;
}

export interface UpdateSkuPayload {
  name: string;
  category: string | null;
  threshold: number | null;
  weightGrams: number | null;
  dimensions: UpdateSkuDimensionsPayload | null;
  description: string | null;
  imageUrl: string | null;
  barcode: string | null;
  brand: string | null;
  isFlashSale: boolean;
}

export interface ListSkusParams {
  search?: string;
  page?: number;
  pageSize?: number;
}

function buildQuery(params: Record<string, string | number | undefined>): string {
  const entries = Object.entries(params).filter(([, v]) => v != null && v !== '');
  if (entries.length === 0) return '';
  const qs = entries
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    .join('&');
  return `?${qs}`;
}

export const inventoryApi = {
  listSkus(params: ListSkusParams = {}) {
    return httpClient.get<PaginatedSkuList>(
      `/api/v1/inventory/skus${buildQuery({ search: params.search, page: params.page, pageSize: params.pageSize })}`,
    );
  },

  ledger(sku: string, limit = 50, cursor?: string | null) {
    return httpClient.get<SkuLedger>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}/ledger${buildQuery({ limit, cursor: cursor ?? undefined })}`,
    );
  },

  update(sku: string, body: UpdateSkuPayload, options: MutationOptions = {}) {
    return httpClient.put<void>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}`,
      body,
      options,
    );
  },

  summary() {
    return httpClient.get<InventorySummary>('/api/v1/inventory/summary');
  },

  adjust(
    body: { sku: string; delta: number; reason: string; note?: string },
    options: MutationOptions = {},
  ) {
    return httpClient.post<void>('/api/v1/inventory/adjustments', body, options);
  },

  setThreshold(sku: string, threshold: number, options: MutationOptions = {}) {
    return httpClient.put<void>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}/threshold`,
      { threshold },
      options,
    );
  },

  setFlashSale(sku: string, active: boolean, options: MutationOptions = {}) {
    return httpClient.put<void>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}/flash-sale`,
      { active },
      options,
    );
  },

  create(body: { sku: string; initialAvailable: number }, options: MutationOptions = {}) {
    return httpClient.post<void>(
      '/api/v1/inventory/skus',
      { sku: body.sku, initialAvailable: body.initialAvailable },
      options,
    );
  },
};

/**
 * Options forwarded to httpClient for mutation calls. Currently exposes
 * `idempotencyKey` so callers can pin the same ULID across retries; the
 * type stays open for future extension (e.g. `signal` for cancellation).
 */
export interface MutationOptions {
  idempotencyKey?: string;
  signal?: AbortSignal;
}
