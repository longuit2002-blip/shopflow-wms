import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { LedgerDrawer } from './LedgerDrawer';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { __resetAuthForTests } from '../../hooks/useAuth';
import type { SkuLedger, SkuListItem } from '../../api/inventory';

const FIXTURE_ITEM: SkuListItem = {
  Sku: 'YS-RED-100',
  Available: 95,
  Reserved: 5,
  Name: 'Yến chưng đường phèn',
  Category: 'finished-goods',
  Threshold: 10,
  IsFlashSale: false,
  Allocations: [],
  P24Outbound: 0,
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
      Items: [
        {
          Id: '01',
          OrderId: 'order-1',
          OrderLineId: 'line-1',
          Status: 'Reserved',
          Quantity: -3,
          Timestamp: '2026-05-18T10:00:00Z',
          RunningBalance: 92,
        },
      ],
      NextCursor: null,
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
      jsonResponse({ Items: [], NextCursor: null }),
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
      jsonResponse({ Items: [], NextCursor: null }),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    expect(screen.getByTestId('alloc-bar-empty')).toBeInTheDocument();
  });

  it('does NOT poll the ledger (asserts exactly one fetch over ~250 ms)', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValue(
      jsonResponse({ Items: [], NextCursor: null }),
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
      jsonResponse({ Items: [], NextCursor: null }),
    );
    renderWithClient(
      <LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} onAdjustClick={() => {}} />,
    );
    expect(screen.getByTestId('ledger-adjust-cta')).toBeInTheDocument();
  });

  it('omits the Adjust CTA when no onAdjustClick handler is wired (read-only consumers)', () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ Items: [], NextCursor: null }),
    );
    renderWithClient(<LedgerDrawer item={FIXTURE_ITEM} onClose={() => {}} />);
    expect(screen.queryByTestId('ledger-adjust-cta')).not.toBeInTheDocument();
  });

  it('clicking the Adjust CTA invokes onAdjustClick with the item SKU', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(
      jsonResponse({ Items: [], NextCursor: null }),
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
});
