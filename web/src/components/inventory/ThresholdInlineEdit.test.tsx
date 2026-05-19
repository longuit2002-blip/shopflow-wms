import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { ThresholdInlineEdit } from './ThresholdInlineEdit';
import { useToast, __resetToastsForTests } from '../../hooks/useToast';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { __resetAuthForTests } from '../../hooks/useAuth';

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({
    defaultOptions: { mutations: { retry: false }, queries: { retry: false } },
  });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

function noBodyResponse(status = 204): Response {
  return new Response(null, { status });
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const fetchMock = () => vi.mocked(globalThis.fetch);

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

describe('ThresholdInlineEdit', () => {
  it('renders the current value as a clickable cell', () => {
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('renders an em-dash when value is null', () => {
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={null} />);
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('—');
  });

  it('clicking the cell enters edit mode and focuses the input', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001');
    expect(input).toBeInTheDocument();
    expect(document.activeElement).toBe(input);
  });

  it('Enter commits the new value via PUT', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);

    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '75');
    await user.keyboard('{Enter}');

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
    const [url, init] = fetchMock().mock.calls[0]!;
    expect(String(url)).toContain('/api/v1/inventory/skus/YN-001/threshold');
    expect((init as RequestInit).method).toBe('PUT');
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({ Threshold: 75 });
  });

  it('blur commits the new value via PUT', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(noBodyResponse());
    renderWithClient(
      <>
        <ThresholdInlineEdit sku="YN-001" value={50} />
        <input data-testid="next-input" />
      </>,
    );
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '75');
    await user.click(screen.getByTestId('next-input'));

    await waitFor(() => {
      expect(fetchMock()).toHaveBeenCalledTimes(1);
    });
  });

  it('Esc reverts WITHOUT calling the API', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '999');
    await user.keyboard('{Escape}');

    expect(fetchMock()).not.toHaveBeenCalled();
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('committing the same value does NOT call the API (no-op short-circuit)', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    await user.keyboard('{Enter}');
    expect(fetchMock()).not.toHaveBeenCalled();
  });

  it('shows the optimistic value immediately on commit (before the network roundtrips)', async () => {
    const user = userEvent.setup();
    let resolveFetch!: (r: Response) => void;
    fetchMock().mockReturnValueOnce(
      new Promise<Response>((res) => {
        resolveFetch = res;
      }),
    );
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '88');
    await user.keyboard('{Enter}');

    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('88');
    // Now resolve the request — no errors should appear.
    act(() => {
      resolveFetch(noBodyResponse());
    });
  });

  it('on a 5xx failure, reverts the optimistic value back to the server value and surfaces an error toast', async () => {
    const user = userEvent.setup();
    fetchMock().mockResolvedValueOnce(jsonResponse({ traceId: 't-1' }, 500));
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);

    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '88');
    await user.keyboard('{Enter}');

    await waitFor(() => {
      expect(useToast.getState().toasts[0]?.kind).toBe('error');
    });
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('rejects negative input by reverting on commit', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    const input = screen.getByTestId('threshold-input-YN-001') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '-5');
    await user.keyboard('{Enter}');
    expect(fetchMock()).not.toHaveBeenCalled();
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveTextContent('50');
  });

  it('the cell button has an aria-label including the SKU (keyboard reachable)', () => {
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    expect(screen.getByTestId('threshold-cell-YN-001')).toHaveAttribute(
      'aria-label',
      expect.stringContaining('YN-001'),
    );
  });

  it('the input has an aria-label including the SKU', async () => {
    const user = userEvent.setup();
    renderWithClient(<ThresholdInlineEdit sku="YN-001" value={50} />);
    await user.click(screen.getByTestId('threshold-cell-YN-001'));
    expect(screen.getByTestId('threshold-input-YN-001')).toHaveAttribute(
      'aria-label',
      expect.stringContaining('YN-001'),
    );
  });
});
