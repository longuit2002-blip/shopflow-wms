/**
 * Orders detail route — Sprint-11 plan U2 gating tests + a11y.
 *
 * Pins the picker button visibility against three orthogonal axes:
 *   1. `usePerm('outbound.orders.pick-confirm')` reactive (KTD3).
 *   2. `detail.currentSagaState === 'AwaitingPick'`.
 *   3. The "just-confirmed" optimistic-hide (DL-007) — covered indirectly
 *      by the disable-during-pending assertions in
 *      useOrderMutations.test.tsx.
 *
 * Plus a single axe-core a11y case asserting zero violations on the
 * AwaitingPick + pick-confirm-perm render (DL-008).
 *
 * Strategy:
 *   - Mock `useOrdersQuery` so the route does not hit fetch.
 *   - Mock `@tanstack/react-router` so `Route.useParams()` resolves
 *     deterministically + `<Link>` becomes an anchor stub.
 *   - Drive perm[] by populating `useAuth` with a JWT carrying the right
 *     claim shape (Sprint-9 KTD1: `perm` as JSON `string[]`).
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { axe } from 'vitest-axe';
import { __resetLocaleForTests } from '../../../hooks/useLocale';
import { __resetAuthForTests, useAuth } from '../../../hooks/useAuth';
import { __resetToastsForTests } from '../../../hooks/useToast';
import type {
  OrderDetailDto,
  OrderTransitionDto,
} from '../../../api/orders';

// ── Mocks ────────────────────────────────────────────────────────────────

const ORDER_ID = '01HABC1234567890ABCDEFGHIJ';

const detailRef = vi.hoisted(() => ({
  current: {
    data: undefined as OrderDetailDto | undefined,
    isLoading: false,
    error: null as unknown,
  },
  transitions: [] as OrderTransitionDto[],
}));

vi.mock('../../../hooks/useOrdersQuery', () => ({
  useOrderDetailQuery: () => ({
    data: detailRef.current.data,
    isLoading: detailRef.current.isLoading,
    error: detailRef.current.error,
  }),
  useOrderTransitionsQuery: () => ({
    data: detailRef.transitions,
    isLoading: false,
    error: null,
  }),
}));

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual<typeof import('@tanstack/react-router')>(
    '@tanstack/react-router',
  );
  return {
    ...actual,
    Link: ({ children, ...props }: { children: ReactNode } & Record<string, unknown>) => (
      // eslint-disable-next-line jsx-a11y/anchor-is-valid
      <a {...(props as Record<string, unknown>)}>{children}</a>
    ),
  };
});

// Stub Route.useParams via a direct override on the imported `Route`.
// `createFileRoute` returns an object — we re-define `useParams` after
// importing so the component reads the test's orderId.
import { Route, OrderDetailRouteComponent } from './$orderId';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(Route as any).useParams = () => ({ orderId: ORDER_ID });

// ── JWTs carrying the perm[] claim shape (Sprint-9 KTD1) ─────────────────

// Payload: { sub, email: "picker@yensao.vn", role: "Picker",
//   tenant_slug: "yensaokhanhhoa",
//   perm: ["outbound.orders.read", "outbound.orders.pick-confirm"],
//   exp: 9999999999 }
const PICKER_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6InBpY2tlckB5ZW5zYW8udm4iLCJyb2xlIjoiUGlja2VyIiwidGVuYW50X3NsdWciOiJ5ZW5zYW9raGFuaGhvYSIsInBlcm0iOlsib3V0Ym91bmQub3JkZXJzLnJlYWQiLCJvdXRib3VuZC5vcmRlcnMucGljay1jb25maXJtIl0sImV4cCI6OTk5OTk5OTk5OX0'
  + '.signature';

// Payload: same as PICKER_JWT but only `outbound.orders.read` in perm[].
const READONLY_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6InZpZXdlckB5ZW5zYW8udm4iLCJyb2xlIjoiVmlld2VyIiwidGVuYW50X3NsdWciOiJ5ZW5zYW9raGFuaGhvYSIsInBlcm0iOlsib3V0Ym91bmQub3JkZXJzLnJlYWQiXSwiZXhwIjo5OTk5OTk5OTk5fQ'
  + '.signature';

// Sprint-12 U3 — Dispatcher JWT. Payload:
//   { sub, email: "dispatcher@yensao.vn", role: "Dispatcher",
//     tenant_slug: "yensaokhanhhoa",
//     perm: ["outbound.orders.read", "outbound.orders.ship-confirm", "hub.connect"],
//     exp: 9999999999 }
// Matches RolePermissionsSeed.DispatcherBaseline exactly (Sprint-12 U1).
const DISPATCHER_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjQiLCJlbWFpbCI6ImRpc3BhdGNoZXJAeWVuc2FvLnZuIiwicm9sZSI6IkRpc3BhdGNoZXIiLCJ0ZW5hbnRfc2x1ZyI6InllbnNhb2toYW5oaG9hIiwicGVybSI6WyJvdXRib3VuZC5vcmRlcnMucmVhZCIsIm91dGJvdW5kLm9yZGVycy5zaGlwLWNvbmZpcm0iLCJodWIuY29ubmVjdCJdLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

// Sprint-12 U3 — Owner JWT (multi-perm). Payload includes both
// pick-confirm AND ship-confirm so AE5's Owner-sees-ConfirmShip-button
// scenario can fire. Real Owner JWTs carry all 24 PermissionKeys.All
// entries; the subset here is the minimum needed for the assertions
// (the JWT-shape contract is the perm[] string presence check, not
// the literal Owner role superset).
const OWNER_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjUiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJwZXJtIjpbIm91dGJvdW5kLm9yZGVycy5yZWFkIiwib3V0Ym91bmQub3JkZXJzLnBpY2stY29uZmlybSIsIm91dGJvdW5kLm9yZGVycy5wYWNrLWNvbmZpcm0iLCJvdXRib3VuZC5vcmRlcnMuc2hpcC1jb25maXJtIiwiaW52ZW50b3J5LnJlYWQiLCJodWIuY29ubmVjdCJdLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

function sessionWith(jwt: string) {
  return {
    accessToken: jwt,
    refreshToken: 'opaque-refresh',
    accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

function fakeDetail(overrides: Partial<OrderDetailDto> = {}): OrderDetailDto {
  return {
    id: ORDER_ID,
    channelExternalOrderId: 'SHOPEE_ORDER_001',
    channel: 'Shopee',
    shippingProfile: 'standard',
    status: 'Pending',
    currentSagaState: 'AwaitingPick',
    expectedWeightTotal: 1500,
    actualWeightTotal: null,
    labelUrl: null,
    trackingNumber: null,
    pickWaveId: null,
    createdAt: new Date().toISOString(),
    updatedAt: null,
    lines: [
      {
        id: 'line-1',
        sku: 'YN-RED-100',
        qty: 2,
        expectedWeight: 750,
      },
    ],
    ...overrides,
  };
}

function renderRoute() {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });
  return render(
    <QueryClientProvider client={qc}>
      <OrderDetailRouteComponent />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  detailRef.current = { data: undefined, isLoading: false, error: null };
  detailRef.transitions = [];
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

// ── Tests ────────────────────────────────────────────────────────────────

describe('orders/$orderId — Sprint-11 U2 picker action gating', () => {
  it('shows ConfirmPick + MarkPickFailed buttons when perm + saga state align', () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'AwaitingPick' });

    renderRoute();

    expect(screen.getByTestId('confirm-pick-button')).toBeInTheDocument();
    expect(screen.getByTestId('mark-pick-failed-button')).toBeInTheDocument();
  });

  it('hides both buttons when user lacks outbound.orders.pick-confirm', () => {
    useAuth.getState().setSession(sessionWith(READONLY_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'AwaitingPick' });

    renderRoute();

    expect(screen.queryByTestId('confirm-pick-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('mark-pick-failed-button')).not.toBeInTheDocument();
  });

  it('hides both buttons when saga state is NOT AwaitingPick (Reserved)', () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'Reserved' });

    renderRoute();

    expect(screen.queryByTestId('confirm-pick-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('mark-pick-failed-button')).not.toBeInTheDocument();
  });

  it('hides both buttons when saga state is Shipped (terminal)', () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'Shipped' });

    renderRoute();

    expect(screen.queryByTestId('confirm-pick-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('mark-pick-failed-button')).not.toBeInTheDocument();
  });

  it('hides both buttons when no session (fail-closed per KTD12)', () => {
    detailRef.current.data = fakeDetail({ currentSagaState: 'AwaitingPick' });
    // No setSession call → useAuth.user is null → usePerm returns false.

    renderRoute();

    expect(screen.queryByTestId('confirm-pick-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('mark-pick-failed-button')).not.toBeInTheDocument();
  });

  it('button labels use the bilingual verb-object form (DL-006)', () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'AwaitingPick' });

    renderRoute();

    const confirm = screen.getByTestId('confirm-pick-button');
    const failBtn = screen.getByTestId('mark-pick-failed-button');
    // Default locale is 'vi' post __resetLocaleForTests; the VI label is
    // "Xác nhận lấy hàng" / "Báo lỗi lấy hàng".
    expect(confirm.textContent).toMatch(/Xác nhận lấy hàng/);
    expect(failBtn.textContent).toMatch(/Báo lỗi lấy hàng/);
  });

  it('action section renders below SagaPipeline + above OrderLineItems (DL-002)', () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'AwaitingPick' });

    renderRoute();

    const saga = screen.getByTestId('order-detail-saga-pipeline');
    const actions = screen.getByTestId('order-detail-pick-actions');
    const lines = screen.getByTestId('order-detail-lines');

    // DOM order: saga → actions → lines.
    expect(saga.compareDocumentPosition(actions)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(actions.compareDocumentPosition(lines)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
  });
});

describe('orders/$orderId — Sprint-11 U2 a11y (DL-008)', () => {
  it('Picker action buttons have zero axe violations on AwaitingPick render', async () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({ currentSagaState: 'AwaitingPick' });

    renderRoute();

    // Scope the axe audit to the picker-actions section only — the
    // wider page tree includes a Sprint-7-baseline `empty-table-header`
    // violation in OrderLineItems (CLAUDE.md "3 pre-existing frontend
    // test failures") that is out of scope for U2.
    const section = screen.getByTestId('order-detail-pick-actions');
    expect(await axe(section)).toHaveNoViolations();
  });
});

// ── Sprint-12 U3 — Dispatcher action gating ──────────────────────────────

describe('orders/$orderId — Sprint-12 U3 dispatcher action gating', () => {
  // KTD2 — Gate reads `Order.status` (Order aggregate field, which DOES
  // reach 'AwaitingShip' via order.MarkAwaitingShip()), NOT
  // `currentSagaState` (saga's CurrentState, which has NO AwaitingShip
  // handler — FulfillmentSaga.cs:213 TODO documents the missing
  // transition). All Sprint-12 visibility scenarios pin `status`, not
  // `currentSagaState`.

  it('AE5: Dispatcher session + status=AwaitingShip → ConfirmShip button visible', () => {
    useAuth.getState().setSession(sessionWith(DISPATCHER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'AwaitingShip',
      currentSagaState: 'Packed', // saga reality per KTD2
    });

    renderRoute();

    expect(screen.getByTestId('confirm-ship-button')).toBeInTheDocument();
  });

  it('AE5: Picker session (no ship-confirm) + status=AwaitingShip → ConfirmShip button hidden', () => {
    useAuth.getState().setSession(sessionWith(PICKER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'AwaitingShip',
      currentSagaState: 'Packed',
    });

    renderRoute();

    expect(screen.queryByTestId('confirm-ship-button')).not.toBeInTheDocument();
  });

  it('AE5: Owner session + status=AwaitingShip → ConfirmShip button visible', () => {
    useAuth.getState().setSession(sessionWith(OWNER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'AwaitingShip',
      currentSagaState: 'Packed',
    });

    renderRoute();

    expect(screen.getByTestId('confirm-ship-button')).toBeInTheDocument();
  });

  it('AE5: Dispatcher session + status=AwaitingPick → ConfirmShip button hidden (state gate fails)', () => {
    useAuth.getState().setSession(sessionWith(DISPATCHER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'AwaitingPick',
      currentSagaState: 'AwaitingPick',
    });

    renderRoute();

    expect(screen.queryByTestId('confirm-ship-button')).not.toBeInTheDocument();
  });

  it('Dispatcher session + status=Packed → ConfirmShip button hidden (transient pre-MarkAwaitingShip)', () => {
    // status moves Packed → AwaitingShip inside the same SaveChanges
    // in PackConfirmAsync, so the Packed window is narrow in practice;
    // gate still must be strict so the button doesn't render briefly.
    useAuth.getState().setSession(sessionWith(DISPATCHER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'Packed',
      currentSagaState: 'Picked',
    });

    renderRoute();

    expect(screen.queryByTestId('confirm-ship-button')).not.toBeInTheDocument();
  });

  it('Dispatcher session + status=Shipped → ConfirmShip button hidden (terminal)', () => {
    useAuth.getState().setSession(sessionWith(DISPATCHER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'Shipped',
      currentSagaState: 'Shipped',
    });

    renderRoute();

    expect(screen.queryByTestId('confirm-ship-button')).not.toBeInTheDocument();
  });

  it('No session → ConfirmShip button hidden (usePerm fails closed)', () => {
    detailRef.current.data = fakeDetail({
      status: 'AwaitingShip',
      currentSagaState: 'Packed',
    });
    // No setSession call → useAuth.user is null → usePerm returns false.

    renderRoute();

    expect(screen.queryByTestId('confirm-ship-button')).not.toBeInTheDocument();
  });
});

// ── Sprint-12 U3 — tracking-pill persistent surface (KTD10, design-F2) ──

describe('orders/$orderId — Sprint-12 U3 tracking-pill (KTD10)', () => {
  it('renders the tracking pill when trackingNumber + labelUrl are set', () => {
    useAuth.getState().setSession(sessionWith(OWNER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'Shipped',
      currentSagaState: 'Shipped',
      trackingNumber: 'SHIP-12345',
      labelUrl: 'https://carrier.test/label/12345.pdf',
    });

    renderRoute();

    const wrap = screen.getByTestId('order-detail-tracking');
    expect(wrap).toBeInTheDocument();
    expect(wrap).toHaveTextContent('SHIP-12345');
    expect(screen.getByTestId('order-detail-tracking-pill')).toBeInTheDocument();
  });

  it('hides the tracking pill when trackingNumber is null (pre-ship state)', () => {
    useAuth.getState().setSession(sessionWith(DISPATCHER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'AwaitingShip',
      currentSagaState: 'Packed',
      trackingNumber: null,
      labelUrl: null,
    });

    renderRoute();

    expect(screen.queryByTestId('order-detail-tracking')).not.toBeInTheDocument();
  });
});

// ── Sprint-12 U3 — a11y smoke test (design-F4 mitigation) ──────────────

describe('orders/$orderId — Sprint-12 U3 a11y', () => {
  it('Ship action button has zero axe violations on AwaitingShip render', async () => {
    useAuth.getState().setSession(sessionWith(DISPATCHER_JWT));
    detailRef.current.data = fakeDetail({
      status: 'AwaitingShip',
      currentSagaState: 'Packed',
    });

    renderRoute();

    // Scope to the ship-actions section, mirroring the Sprint-11
    // pick-actions axe scope — wider page tree includes a Sprint-7
    // baseline `empty-table-header` violation in OrderLineItems that
    // is out of scope for U3.
    const section = screen.getByTestId('order-detail-ship-actions');
    expect(await axe(section)).toHaveNoViolations();
  });
});
