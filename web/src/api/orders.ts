/**
 * Typed Orders API client — Sprint-7 U4 backend contract.
 *
 * Consumed by Sprint-7 U10/U13 (Orders list + detail screens). All requests
 * go through `httpClient` so Authorization + X-Tenant-Slug + (for mutations)
 * Idempotency-Key are attached automatically.
 *
 * Wire-shape notes (Sprint-7.5 U1+U2):
 *   - Backend now serialises with `JsonNamingPolicy.CamelCase` across MVC +
 *     SignalR; types below mirror the camelCase shape verbatim. The matching
 *     C# DTOs live at
 *     `src/Services/Outbound/ShopFlow.Outbound.Api/Contracts/OrderDtos.cs`.
 *   - `OrderListItemDto.age` is a TimeSpan on the server. System.Text.Json
 *     emits it as `"hh:mm:ss[.fffffff]"` by default — typed as `string`
 *     here and rendered as-is by the list table.
 *   - `OrderListItemDto.lastTransitionAt` and `OrderTransitionDto.occurredAt`
 *     serialise to ISO 8601 UTC strings.
 *   - `OrderTransitionDto.correlationId` is the W3C correlation id stamped
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

// ── DTOs (camelCase — Sprint-7.5 wire normalisation) ─────────────────────

export interface OrderListItemDto {
  id: string;
  channelExternalOrderId: string;
  /** Parsed prefix label: "Shopee" | "Lazada" | "TikTok Shop" | "Direct". */
  channel: string;
  lineCount: number;
  /** Saga state string; null until the first OrderPlacedV1 consume lands. */
  currentSagaState: string | null;
  /** TimeSpan rendered by System.Text.Json (e.g. "03:42:17.1234567"). */
  age: string;
  /** ISO 8601 UTC timestamp; null when no transitions have been recorded. */
  lastTransitionAt: string | null;
}

export interface OrderListResponse {
  items: OrderListItemDto[];
  totalCount: number;
}

export interface OrderLineResponse {
  id: string;
  sku: string;
  qty: number;
  expectedWeight: number | null;
}

/**
 * Sprint-3-redux POST/GET response shape — returned by the seed endpoint
 * and the original create/retrieve paths. Lacks the Sprint-7-U4 detail
 * extensions (channel / currentSagaState / createdAt / updatedAt) so it's
 * a distinct type rather than an alias.
 */
export interface OrderResponse {
  id: string;
  channelExternalOrderId: string;
  shippingProfile: string;
  status: string;
  expectedWeightTotal: number | null;
  actualWeightTotal: number | null;
  labelUrl: string | null;
  trackingNumber: string | null;
  pickWaveId: string | null;
  lines: OrderLineResponse[];
}

export interface OrderDetailDto {
  id: string;
  channelExternalOrderId: string;
  channel: string;
  shippingProfile: string;
  status: string;
  currentSagaState: string | null;
  expectedWeightTotal: number | null;
  actualWeightTotal: number | null;
  labelUrl: string | null;
  trackingNumber: string | null;
  pickWaveId: string | null;
  /** ISO 8601 UTC timestamp. */
  createdAt: string;
  /** ISO 8601 UTC timestamp; null when not yet updated. */
  updatedAt: string | null;
  lines: OrderLineResponse[];
}

/**
 * Sprint-12 U2 — POST /api/outbound/orders/{id}/confirm-ship response.
 * Mirrors the backend `ConfirmShipResponse` record
 * (`src/Services/Outbound/ShopFlow.Outbound.Api/Contracts/OrderDtos.cs`).
 * The label URL + tracking number are surfaced in the success toast and
 * re-rendered on the order-detail header (KTD10 persistent tracking-pill)
 * via the post-confirm detail refetch.
 */
export interface ConfirmShipResponse {
  labelUrl: string;
  trackingNumber: string;
  order: OrderResponse;
}

export interface OrderTransitionDto {
  id: string;
  orderId: string;
  fromState: string;
  toState: string;
  /** ISO 8601 UTC timestamp. */
  occurredAt: string;
  eventType: string;
  /** W3C correlation id stamped by the saga middleware — doc-review #3. */
  correlationId: string;
}

export interface OrderKpiResponse {
  activeOrders: number;
  awaitingPick: number;
  awaitingShip: number;
  failedToday: number;
}

export interface SeedOrderRequest {
  /** Synthesized line count; default 3 server-side, clamped 1-50. */
  lineCount?: number;
  /** Optional channel-id prefix override (e.g. "SHOPEE_"). */
  channelPrefix?: string;
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
    // channel/currentSagaState/createdAt). The follow-on detail fetch
    // (`fetchOrderDetail(id)`) is the way to read the U7 enrichment.
    return httpClient.post<OrderResponse>(
      `${BASE}/seed`,
      // Forward only the fields the caller set so the server's
      // record-default values (lineCount=3, channelPrefix=null) apply.
      payload,
      options,
    );
  },

  /**
   * Sprint-11 U2 — Picker confirm-pick action.
   * POST /api/outbound/orders/{id}/confirm-pick. Backend gated by
   * [Authorize(Policy="outbound.orders.pick-confirm")] (Sprint-10/-11
   * backend U1). Empty body; Idempotency-Key on retry replays the audit.
   */
  confirmPick(orderId: string, options: MutationOptions = {}) {
    return httpClient.post<OrderResponse>(
      `${BASE}/${encodeURIComponent(orderId)}/confirm-pick`,
      {},
      options,
    );
  },

  /**
   * Sprint-11 U2 — Picker mark-pick-failed action.
   * POST /api/outbound/orders/{id}/mark-pick-failed with { reason } body.
   * Backend gated by [Authorize(Policy="outbound.orders.pick-confirm")];
   * reason is the picker-supplied free-text captured by the modal.
   */
  markPickFailed(
    orderId: string,
    reason: string,
    options: MutationOptions = {},
  ) {
    return httpClient.post<OrderResponse>(
      `${BASE}/${encodeURIComponent(orderId)}/mark-pick-failed`,
      { reason },
      options,
    );
  },

  /**
   * Sprint-12 U2 — Dispatcher confirm-ship action.
   * POST /api/outbound/orders/{id}/confirm-ship. Backend gated by
   * [Authorize(Policy="outbound.orders.ship-confirm")] (Sprint-10 U2);
   * pre-state requirement is `OrderStatus.AwaitingShip`. Empty body;
   * Idempotency-Key threaded via options. Response carries the carrier
   * label URL + tracking number alongside the post-confirm order shape.
   */
  confirmShip(orderId: string, options: MutationOptions = {}) {
    return httpClient.post<ConfirmShipResponse>(
      `${BASE}/${encodeURIComponent(orderId)}/confirm-ship`,
      {},
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
export const confirmPickOrder = (orderId: string, options?: MutationOptions) =>
  ordersApi.confirmPick(orderId, options);
export const markPickFailedOrder = (
  orderId: string,
  reason: string,
  options?: MutationOptions,
) => ordersApi.markPickFailed(orderId, reason, options);
