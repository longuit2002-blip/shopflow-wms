/**
 * Typed Inventory API client — Sprint-6 U7/U8 backend contract.
 *
 * Consumed across U9-U12. All requests go through `httpClient` so
 * Authorization + X-Tenant-Slug + (for mutations) Idempotency-Key are
 * attached automatically.
 *
 * Contract drift caveats:
 *   - Sprint-6 backend serializes records with PascalCase property
 *     names by default (no `JsonNamingPolicy.CamelCase` configured at
 *     the kestrel layer; sets ProblemDetails only). Frontend types
 *     mirror the exact wire shape (PascalCase) to avoid silent null
 *     bindings. Sprint-7 normalizes the JSON policy to camelCase.
 */

import { httpClient } from './httpClient';

export interface ChannelAllocation {
  Channel: string;
  Allocated: number;
}

export interface SkuListItem {
  Sku: string;
  Available: number;
  Reserved: number;
  Name: string | null;
  Category: string | null;
  Threshold: number | null;
  IsFlashSale: boolean;
  Allocations: ChannelAllocation[];
  P24Outbound: number;
}

export interface PaginatedSkuList {
  Items: SkuListItem[];
  Page: number;
  PageSize: number;
  Total: number;
}

export interface SkuLedgerEntry {
  Id: string;
  OrderId: string;
  OrderLineId: string;
  Status: string;
  Quantity: number;
  Timestamp: string;
  RunningBalance: number;
}

export interface SkuLedger {
  Items: SkuLedgerEntry[];
  NextCursor: string | null;
}

export interface InventorySummary {
  TotalSkus: number;
  TotalAvailable: number;
  TotalReserved: number;
  BelowThresholdCount: number;
  OversellRiskCount: number;
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

  ledger(sku: string, limit = 100) {
    return httpClient.get<SkuLedger>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}/ledger${buildQuery({ limit })}`,
    );
  },

  summary() {
    return httpClient.get<InventorySummary>('/api/v1/inventory/summary');
  },

  adjust(body: { sku: string; delta: number; reason: string; note?: string }) {
    return httpClient.post<void>('/api/v1/inventory/adjustments', body);
  },

  setThreshold(sku: string, threshold: number) {
    return httpClient.put<void>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}/threshold`,
      { Threshold: threshold },
    );
  },

  setFlashSale(sku: string, active: boolean) {
    return httpClient.put<void>(
      `/api/v1/inventory/skus/${encodeURIComponent(sku)}/flash-sale`,
      { Active: active },
    );
  },

  create(body: { sku: string; initialAvailable: number }) {
    return httpClient.post<void>(
      '/api/v1/inventory/skus',
      { Sku: body.sku, InitialAvailable: body.initialAvailable },
    );
  },
};
