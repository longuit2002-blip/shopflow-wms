/**
 * OrdersFilterStrip tests — Sprint-7.5 plan U7 URL-state migration.
 *
 * Sprint-7 didn't ship a test file for this strip; U7 introduces one
 * because the strip is now load-bearing for the URL-search-params
 * adoption. The strip itself stays a controlled `value`/`onChange` pair;
 * the parent route (`routes/_auth/orders/index.tsx`) maps the URL-state
 * to the OrdersFilter shape and writes patches back via the helper.
 *
 * Scenarios:
 *   1. Controlled status select reflects the prop and emits the patch.
 *   2. Empty option (`""`) clears the filter field via undefined.
 *   3. Channel select round-trips through onChange.
 *   4. Search input emits substring patches per keystroke.
 *   5. The merged onChange payload omits empty-string fields (the strip's
 *      internal `patch()` helper drops them; this is the contract the
 *      route relies on so the URL stays clean).
 *   6. Locale flip swaps Vietnamese → English labels.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OrdersFilterStrip } from './OrdersFilterStrip';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';
import type { OrdersFilter } from '../../api/orders';

beforeEach(() => {
  __resetLocaleForTests();
});

afterEach(() => {
  __resetLocaleForTests();
});

describe('OrdersFilterStrip', () => {
  it('reflects the controlled `value.status` prop on the status select', () => {
    render(
      <OrdersFilterStrip
        value={{ status: 'AwaitingPick' }}
        onChange={() => {}}
      />,
    );
    const sel = screen.getByTestId('orders-filter-status') as HTMLSelectElement;
    expect(sel.value).toBe('AwaitingPick');
  });

  it('emits an onChange patch when status changes (URL-state seam)', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<OrdersFilterStrip value={{}} onChange={onChange} />);

    await user.selectOptions(
      screen.getByTestId('orders-filter-status') as HTMLSelectElement,
      'Reserved',
    );

    expect(onChange).toHaveBeenCalledTimes(1);
    const arg = onChange.mock.calls[0][0] as OrdersFilter;
    expect(arg.status).toBe('Reserved');
  });

  it('selecting the All option clears the status field from the emitted patch', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <OrdersFilterStrip
        value={{ status: 'Reserved' }}
        onChange={onChange}
      />,
    );

    await user.selectOptions(
      screen.getByTestId('orders-filter-status') as HTMLSelectElement,
      '',
    );

    const arg = onChange.mock.calls[0][0] as OrdersFilter;
    // The strip's internal patch() helper drops empty-string fields, so
    // the URL helper sees `status` absent → treats as default → omits.
    expect('status' in arg).toBe(false);
  });

  it('channel select emits the wire-shape value upstream', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<OrdersFilterStrip value={{}} onChange={onChange} />);

    await user.selectOptions(
      screen.getByTestId('orders-filter-channel') as HTMLSelectElement,
      'SHOPEE',
    );

    const arg = onChange.mock.calls[0][0] as OrdersFilter;
    expect(arg.channel).toBe('SHOPEE');
  });

  it('search input emits patches per keystroke', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<OrdersFilterStrip value={{}} onChange={onChange} />);

    await user.type(
      screen.getByTestId('orders-filter-search') as HTMLInputElement,
      'SHO',
    );

    expect(onChange).toHaveBeenCalledTimes(3);
    const lastArg = onChange.mock.lastCall?.[0] as OrdersFilter;
    expect(lastArg.search).toBe('O');
  });

  it('switches to English copy when locale flips', () => {
    setLang('en');
    render(<OrdersFilterStrip value={{}} onChange={() => {}} />);

    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Channel')).toBeInTheDocument();
    expect(screen.getByText('Since')).toBeInTheDocument();
    expect(screen.getByText('Until')).toBeInTheDocument();
  });

  it('preserves existing field values when patching a sibling field', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <OrdersFilterStrip
        value={{ status: 'Reserved' }}
        onChange={onChange}
      />,
    );

    await user.selectOptions(
      screen.getByTestId('orders-filter-channel') as HTMLSelectElement,
      'LAZADA',
    );

    const arg = onChange.mock.calls[0][0] as OrdersFilter;
    expect(arg.status).toBe('Reserved');
    expect(arg.channel).toBe('LAZADA');
  });
});
