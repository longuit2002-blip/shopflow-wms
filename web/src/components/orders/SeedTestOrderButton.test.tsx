/**
 * SeedTestOrderButton — Sprint-10.5 U5 perm-gating tests.
 *
 * Sprint-7 U10 shipped the button DEV-gated via `import.meta.env.DEV`.
 * Sprint-10.5 U5 adds a second gate: `usePerm('outbound.orders.write')`.
 * Both must be true. Vitest runs with `import.meta.env.DEV=true`, so the
 * DEV gate is implicitly satisfied — these tests focus on the perm gate.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { SeedTestOrderButton } from './SeedTestOrderButton';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { __resetToastsForTests } from '../../hooks/useToast';
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

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  __resetToastsForTests();
  vi.unstubAllGlobals();
});

describe('SeedTestOrderButton (Sprint-10.5 U5 perm gating)', () => {
  it('renders the button when DEV + the user holds outbound.orders.write', () => {
    useAuth.getState().setSession(sessionWith(['outbound.orders.write']));
    const { getByTestId } = renderWithClient(<SeedTestOrderButton />);
    expect(getByTestId('seed-test-order')).toBeInTheDocument();
  });

  it('renders nothing when the user lacks outbound.orders.write (hidden — KTD8)', () => {
    useAuth.getState().setSession(sessionWith(['outbound.orders.read']));
    const { container } = renderWithClient(<SeedTestOrderButton />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when the user has no session (fail-closed — KTD12)', () => {
    // No setSession call.
    const { container } = renderWithClient(<SeedTestOrderButton />);
    expect(container).toBeEmptyDOMElement();
  });
});
