/**
 * Typed Orders API client — Sprint-7 U4 backend contract.
 *
 * Consumed by Sprint-7 U10/U13 (Orders list + detail screens). All requests
 * go through `httpClient` so Authorization + X-Tenant-Slug + (for mutations)
 * Idempotency-Key are attached automatically.
 *
 * Wire-shape notes:
 *   - PascalCase matches the Sprint-6 KTD4 convention — the .NET serializer
 *     emits properties verbatim from the record definition. The matching
 *     C# DTOs live at
 *     `src/Services/Outbound/ShopFlow.Outbound.Api/Contracts/OrderDtos.cs`.
 *   - `OrderListItemDto.Age` is a TimeSpan on the server. System.Text.Json
 *     emits it as `"hh:mm:ss[.fffffff]"` by default — typed as `string`
 *     here and rendered as-is by the list table.
 *   - `OrderListItemDto.LastTransitionAt` and `OrderTransitionDto.OccurredAt`
 *     serialize to ISO 8601 UTC strings.
 *   - `OrderTransitionDto.CorrelationId` is the W3C correlation id stamped
 *     by the saga middleware (doc-review decision #3); routed straight to
 *     the trace explorer hyperlink by R14.
 */

import { httpClient } from './httpClient';

/**
 * Options forwarded to httpClient for mutation calls. Mirrors the
 * `MutationOptions` shape in `./inventory.ts` so the two surfaces stay in
 * lockstep — callers can pin an idempotency key across retries and/or
 * pass an AbortSignal.
 */
export interface MutationOptions {
  idempotencyKey?: string;
  signal?: AbortSignal;
}

// ── DTOs (PascalCase — mirror the C# wire exactly) ───────────────────────

export interface OrderListItemDto {
  Id: string;
  ChannelExternalOrderId: string;
  /** Parsed prefix label: "Shopee" | "Lazada" | "TikTok Shop" | "Direct". */
  Channel: string;
  LineCount: number;
  /** Saga state string; null until the first OrderPlacedV1 consume lands. */
  CurrentSagaState: string | null;
  /** TimeSpan rendered by System.Text.Json (e.g. "03:42:17.1234567"). */
  Age: string;
  /** ISO 8601 UTC timestamp; null when no transitions have been recorded. */
  LastTransitionAt: string | null;
}

export interface OrderListResponse {
  Items: OrderListItemDto[];
  TotalCount: number;
}

export interface OrderLineResponse {
  Id: string;
  Sku: string;
  Qty: number;
  ExpectedWeight: number | null;
}

/**
 * Sprint-3-redux POST/GET response shape — returned by the seed endpoint
 * and the original create/retrieve paths. Lacks the Sprint-7-U4 detail
 * extensions (Channel / CurrentSagaState / CreatedAt / UpdatedAt) so it's
 * a distinct type rather than an alias.
 */
export interface OrderResponse {
  Id: string;
  ChannelExternalOrderId: string;
  ShippingProfile: string;
  Status: string;
  ExpectedWeightTotal: number | null;
  ActualWeightTotal: number | null;
  LabelUrl: string | null;
  TrackingNumber: string | null;
  PickWaveId: string | null;
  Lines: OrderLineResponse[];
}

export interface OrderDetailDto {
  Id: string;
  ChannelExternalOrderId: string;
  Channel: string;
  ShippingProfile: string;
  Status: string;
  CurrentSagaState: string | null;
  ExpectedWeightTotal: number | null;
  ActualWeightTotal: number | null;
  LabelUrl: string | null;
  TrackingNumber: string | null;
  PickWaveId: string | null;
  /** ISO 8601 UTC timestamp. */
  CreatedAt: string;
  /** ISO 8601 UTC timestamp; null when not yet updated. */
  UpdatedAt: string | null;
  Lines: OrderLineResponse[];
}

export interface OrderTransitionDto {
  Id: string;
  OrderId: string;
  FromState: string;
  ToState: string;
  /** ISO 8601 UTC timestamp. */
  OccurredAt: string;
  EventType: string;
  /** W3C correlation id stamped by the saga middleware — doc-review #3. */
  CorrelationId: string;
}

export interface OrderKpiResponse {
  ActiveOrders: number;
  AwaitingPick: number;
  AwaitingShip: number;
  FailedToday: number;
}

export interface SeedOrderRequest {
  /** Synthesized line count; default 3 server-side, clamped 1-50. */
  LineCount?: number;
  /** Optional channel-id prefix override (e.g. "SHOPEE_"). */
  ChannelPrefix?: string;
}

// ── Filter shape consumed by the list endpoint ───────────────────────────

export interface OrdersFilter {
  /** Saga state string (e.g. "Reserved", "AwaitingPick"). */
  status?: string;
  /** Channel-prefix substring (e.g. "SHOPEE", "LAZADA"). */
  channel?: string;
  /** Free-text search across channel-external-order-id. */
  search?: string;
  /** Lower bound ISO 8601 UTC timestamp on `orders.created_at`. */
  since?: string;
  /** Upper bound ISO 8601 UTC timestamp on `orders.created_at`. */
  until?: string;
  /** Pagination offset; default 0 server-side. */
  skip?: number;
  /** Page size; default 50 server-side (DefaultListTake). */
  take?: number;
}

// ── Query-string helpers ─────────────────────────────────────────────────

function buildQuery(params: Record<string, string | number | undefined>): string {
  const entries = Object.entries(params).filter(
    ([, v]) => v !== undefined && v !== null && v !== '',
  );
  if (entries.length === 0) return '';
  const qs = entries
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    .join('&');
  return `?${qs}`;
}

// ── Fetch functions ──────────────────────────────────────────────────────

const BASE = '/api/outbound/orders';

export const ordersApi = {
  list(filter: OrdersFilter = {}) {
    return httpClient.get<OrderListResponse>(
      `${BASE}${buildQuery({
        status: filter.status,
        channel: filter.channel,
        search: filter.search,
        since: filter.since,
        until: filter.until,
        skip: filter.skip,
        take: filter.take,
      })}`,
    );
  },

  kpis() {
    return httpClient.get<OrderKpiResponse>(`${BASE}/kpis`);
  },

  detail(orderId: string) {
    return httpClient.get<OrderDetailDto>(`${BASE}/${encodeURIComponent(orderId)}`);
  },

  transitions(orderId: string) {
    return httpClient.get<OrderTransitionDto[]>(
      `${BASE}/${encodeURIComponent(orderId)}/transitions`,
    );
  },

  seed(payload: SeedOrderRequest = {}, options: MutationOptions = {}) {
    // The backend returns `OrderResponse` (Sprint-3-redux shape — no
    // Channel/CurrentSagaState/CreatedAt). The follow-on detail fetch
    // (`fetchOrderDetail(id)`) is the way to read the U7 enrichment.
    return httpClient.post<OrderResponse>(
      `${BASE}/seed`,
      // Forward only the fields the caller set so the server's
      // record-default values (LineCount=3, ChannelPrefix=null) apply.
      payload,
      options,
    );
  },
};

// Named exports for tree-shake-friendly imports at the call site.
export const fetchOrders = (filter: OrdersFilter = {}) => ordersApi.list(filter);
export const fetchOrderKpis = () => ordersApi.kpis();
export const fetchOrderDetail = (orderId: string) => ordersApi.detail(orderId);
export const fetchOrderTransitions = (orderId: string) => ordersApi.transitions(orderId);
export const seedOrder = (payload?: SeedOrderRequest, options?: MutationOptions) =>
  ordersApi.seed(payload, options);
