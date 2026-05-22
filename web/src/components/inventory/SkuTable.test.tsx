/**
 * SkuTable — Sprint-10.5 U5 perm-gating tests.
 *
 * Sprint-7.5 U4 added the per-row Edit button gated on the parent route
 * passing `onEditClick`. Sprint-10.5 U5 adds an internal `usePerm`
 * defence-in-depth: even when the route passes `onEditClick`, the row
 * Edit affordance is suppressed when the JWT perm[] lacks
 * `inventory.skus.write`. This is DL-003-aligned — the existing
 * conditional-column logic remains the single seam, and the gate flips
 * the effective `onEditClick` to undefined when the perm is missing.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { SkuTable } from './SkuTable';
import type { SkuListItem } from '../../api/inventory';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({
    defaultOptions: { mutations: { retry: false }, queries: { retry: false } },
  });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

function jwtWithPerm(perm: string[]): string {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const payload = btoa(
    JSON.stringify({
      sub: '8f72f516-cc02-4f54-9cc8-be0f3b96c4f3',
      email: 'owner@yensao.vn',
      role: 'Owner',
      tenant_slug: 'yensaokhanhhoa',
      perm,
      exp: 9999999999,
    }),
  );
  return `${header}.${payload}.signature`;
}

function sessionWith(perm: string[]) {
  return {
    accessToken: jwtWithPerm(perm),
    refreshToken: 'opaque',
    accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
  };
}

const ROW: SkuListItem = {
  sku: 'YN-001',
  name: 'Tổ yến tinh chế',
  category: null,
  available: 100,
  reserved: 0,
  threshold: 25,
  isFlashSale: false,
  allocations: [],
  p24Outbound: 0,
};

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
});

describe('SkuTable (Sprint-10.5 U5 perm gating)', () => {
  // The threshold cell triggers `usePerm('inventory.skus.threshold.write')`
  // inside ThresholdInlineEdit — seed both keys so the column renders as
  // expected for the perm-holding scenarios.
  const FULL_PERMS = ['inventory.skus.write', 'inventory.skus.threshold.write'];

  it('renders the Edit column when the user holds inventory.skus.write AND parent passes onEditClick', () => {
    useAuth.getState().setSession(sessionWith(FULL_PERMS));
    const { queryByTestId } = renderWithClient(
      <SkuTable items={[ROW]} onSelectSku={() => {}} onEditClick={vi.fn()} />,
    );
    expect(queryByTestId('sku-edit-YN-001')).toBeInTheDocument();
  });

  it('hides the Edit column when the user lacks inventory.skus.write (column + row both suppressed)', () => {
    useAuth.getState().setSession(sessionWith(['inventory.read']));
    const { queryByTestId, queryByLabelText } = renderWithClient(
      <SkuTable items={[ROW]} onSelectSku={() => {}} onEditClick={vi.fn()} />,
    );
    // Per-row Edit button gone.
    expect(queryByTestId('sku-edit-YN-001')).not.toBeInTheDocument();
    // <th aria-label="Actions"> column header also gone — DL-003 keeps
    // the single seam between <th> and <td> visibility.
    expect(queryByLabelText(/Thao tác|Actions/i)).not.toBeInTheDocument();
  });

  it('hides the Edit column when the user has no session (fail-closed — KTD12)', () => {
    // No setSession call.
    const { queryByTestId, queryByLabelText } = renderWithClient(
      <SkuTable items={[ROW]} onSelectSku={() => {}} onEditClick={vi.fn()} />,
    );
    expect(queryByTestId('sku-edit-YN-001')).not.toBeInTheDocument();
    expect(queryByLabelText(/Thao tác|Actions/i)).not.toBeInTheDocument();
  });

  it('does NOT render the Edit column when the parent omits onEditClick, even with the perm', () => {
    // The existing Sprint-7.5 contract: prop absent → column suppressed.
    useAuth.getState().setSession(sessionWith(FULL_PERMS));
    const { queryByTestId } = renderWithClient(
      <SkuTable items={[ROW]} onSelectSku={() => {}} />,
    );
    expect(queryByTestId('sku-edit-YN-001')).not.toBeInTheDocument();
  });
});
