import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { LedgerDrawer } from './LedgerDrawer';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { __resetAuthForTests } from '../../hooks/useAuth';
import type { SkuLedger, SkuListItem } from '../../api/inventory';

const FIXTURE_ITEM: SkuListItem = {
  sku: 'YS-RED-100',
  available: 95,
  reserved: 5,
  name: 'Yến chưng đường phèn',
  category: 'finished-goods',
  threshold: 10,
  isFlashSale: false,
  allocations: [],
  p24Outbound: 0,
};

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  __resetLocaleForTests();
  __resetAuthForTests();
  vi.unstubAllGlobals();
});

describe('LedgerDrawer', () => {
  it('renders nothing when item is null', () => {
    const { container } = renderWithClient(
      <LedgerDrawer item={null} onClose={() => {}} />,
    );
    expect(container).toBeEmptyDOMElement();
    expect(globalThis.fetch).not.toHaveBeenCalled();
  });

  it('opens with title containing the SKU and fetches the ledger', async () => {
    const fixture: SkuLedger = {
      items: [
        {
          id: '01',
          orderId: 'order-1',
          orderLineId: 'line-1',
          status: 'Reserved',
          quantity: -3,
          timestamp: '2026-05-18T10:00:00Z',
          runningBalance: 92,
        },
      ],
      nextCursor: null,
    };
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(jsonResponse(fixture));

    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
    expect(screen.getByText(/YS-RED-100/)).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Đã giữ')).toBeInTheDocument();
    });
    const [url] = vi.mocked(globalThis.fetch).mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/skus/YS-RED-100/ledger');
  });

  it('shows the empty-state when the ledger has no entries', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    await waitFor(() => {
      expect(screen.getByTestId('ledger-empty')).toBeInTheDocument();
    });
  });

  it('shows the error state when the ledger fetch fails', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ title: 'boom' }, 500),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    await waitFor(() => {
      expect(screen.getByTestId('ledger-error')).toBeInTheDocument();
    });
  });

  it('renders the AllocationBar placeholder when item ships with no allocations', () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    expect(screen.getByTestId('alloc-bar-empty')).toBeInTheDocument();
  });

  it('does NOT poll the ledger (asserts exactly one fetch over ~250 ms)', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    await waitFor(() => {
      expect(screen.getByTestId('ledger-empty')).toBeInTheDocument();
    });
    await new Promise((r) => setTimeout(r, 250));
    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
  });

  it('renders the "Điều chỉnh tồn" CTA when onAdjustClick is provided', () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(
      <LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} onAdjustClick={() => {}} />,
    );
    expect(screen.getByTestId('ledger-adjust-cta')).toBeInTheDocument();
  });

  it('omits the Adjust CTA when no onAdjustClick handler is wired (read-only consumers)', () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    expect(screen.queryByTestId('ledger-adjust-cta')).not.toBeInTheDocument();
  });

  it('clicking the Adjust CTA invokes onAdjustClick with the item SKU', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    const onAdjustClick = vi.fn();
    const user = (await import('@testing-library/user-event')).default.setup();
    renderWithClient(
      <LedgerDrawer
        item={FIXTURE_ITEM}
        onClose={() => {}}
        onAdjustClick={onAdjustClick}
      />,
    );
    await user.click(screen.getByTestId('ledger-adjust-cta'));
    expect(onAdjustClick).toHaveBeenCalledWith('YS-RED-100');
  });

  it('mounts the FlashSaleToggle in the drawer header reflecting the IsFlashSale flag', () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(
      <LedgerDrawer
        item={{ ...FIXTURE_ITEM, isFlashSale: true }}
        onClose={() => {}}
      />,
    );
    expect(screen.getByTestId('flash-toggle-YS-RED-100')).toHaveAttribute(
      'aria-checked',
      'true',
    );
  });

  // ── Sprint-7.5 U7: URL-driven open + stale deep-link recovery (D-005) ──

  it('opens via selectedSku even when item is null (URL-driven open seam)', async () => {
    // The parent route renders the drawer when `?selected=` is set but the
    // SKU's full record hasn't arrived yet (loading the inventory list).
    // The drawer must still mount and kick off the ledger fetch using the
    // URL-selected SKU.
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(
      <LedgerDrawer
        item={null}
        selectedSku="YS-RED-100"
        onClose={() => {}}
      />,
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText(/YS-RED-100/)).toBeInTheDocument();
  });

  it('renders the D-005 unknown-SKU error state when isUnknownSelectedSku is true', async () => {
    // Stale deep-link: `?selected=` points at a SKU that no longer exists.
    // The parent route detects the miss after loading and flips the
    // `isUnknownSelectedSku` flag; the drawer renders an explicit error
    // panel with a Close button. The URL param is NOT auto-cleared.
    renderWithClient(
      <LedgerDrawer
        item={null}
        selectedSku="SKU-NONEXISTENT"
        isUnknownSelectedSku={true}
        onClose={() => {}}
      />,
    );

    expect(screen.getByTestId('ledger-unknown-sku')).toBeInTheDocument();
    // SKU name appears both in the drawer title and in the unknown-SKU
    // body — assert via the scoped error panel rather than a global getByText.
    expect(
      within(screen.getByTestId('ledger-unknown-sku')).getByText(/SKU-NONEXISTENT/),
    ).toBeInTheDocument();
    expect(screen.getByTestId('ledger-unknown-sku-close')).toBeInTheDocument();
  });

  it('the D-005 Close button invokes onClose (parent then clears ?selected=)', async () => {
    const onClose = vi.fn();
    const user = (await import('@testing-library/user-event')).default.setup();
    renderWithClient(
      <LedgerDrawer
        item={null}
        selectedSku="SKU-NONEXISTENT"
        isUnknownSelectedSku={true}
        onClose={onClose}
      />,
    );

    await user.click(screen.getByTestId('ledger-unknown-sku-close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('renders the "All entries loaded" end-of-list indicator when nextCursor is null', async () => {
    const fixture: SkuLedger = {
      items: [
        {
          id: '01',
          orderId: 'order-1',
          orderLineId: 'line-1',
          status: 'Reserved',
          quantity: -3,
          timestamp: '2026-05-18T10:00:00Z',
          runningBalance: 92,
        },
      ],
      nextCursor: null,
    };
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(jsonResponse(fixture));

    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('ledger-end')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('ledger-load-more')).not.toBeInTheDocument();
  });

  it('renders the "Load more" button when nextCursor is non-null and advances cursor on click', async () => {
    const user = (await import('@testing-library/user-event')).default.setup();
    const page1: SkuLedger = {
      items: [
        {
          id: '01',
          orderId: 'order-1',
          orderLineId: 'line-1',
          status: 'Reserved',
          quantity: -3,
          timestamp: '2026-05-18T10:00:00Z',
          runningBalance: 92,
        },
      ],
      nextCursor: 'cursor-page-2',
    };
    const page2: SkuLedger = {
      items: [
        {
          id: '02',
          orderId: 'order-2',
          orderLineId: 'line-1',
          status: 'Confirmed',
          quantity: -2,
          timestamp: '2026-05-18T11:00:00Z',
          runningBalance: 90,
        },
      ],
      nextCursor: null,
    };
    vi.mocked(globalThis.fetch)
      .mockResolvedValueOnce(jsonResponse(page1))
      .mockResolvedValueOnce(jsonResponse(page2));

    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);

    const loadMore = await screen.findByTestId('ledger-load-more');
    expect(loadMore).toBeInTheDocument();

    await user.click(loadMore);

    // After advancing, page 2 fetches with the cursor + replaces with end-state.
    await waitFor(() => {
      expect(screen.getByTestId('ledger-end')).toBeInTheDocument();
    });
    // Second fetch carried the cursor query param.
    const calls = vi.mocked(globalThis.fetch).mock.calls;
    expect(calls).toHaveLength(2);
    const secondCallUrl = String(calls[1][0]);
    expect(secondCallUrl).toContain('cursor=cursor-page-2');
  });

  it('ledgerCursor prop is accepted but does not affect the U7 fetch shape (U6 seam)', async () => {
    // U6 will wire the cursor into useSkuLedgerQuery; for now the prop is
    // plumbed end-to-end without changing the URL of the fetch call. This
    // test pins the contract so U6 can detect when it lands the change.
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ items: [], nextCursor: null }),
    );
    renderWithClient(
      <LedgerDrawer
        item={FIXTURE_ITEM}
        ledgerCursor="cursor-abc"
        onClose={() => {}}
      />,
    );
    await waitFor(() => {
      expect(globalThis.fetch).toHaveBeenCalled();
    });
    const [url] = vi.mocked(globalThis.fetch).mock.calls[0]!;
    // The URL does NOT yet carry `?cursor=` — U6 will change this.
    expect(String(url)).not.toContain('cursor=');
  });
});
