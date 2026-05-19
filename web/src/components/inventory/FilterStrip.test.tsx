/**
 * FilterStrip tests — Sprint-7.5 plan U7 URL-state migration.
 *
 * Sprint-6 didn't ship a FilterStrip test file; U7 introduces it because
 * the strip is now load-bearing for the URL-search-params adoption (the
 * route writes via the `onSearchChange` callback, the URL is the source
 * of truth, and reload restores the value via the controlled `search`
 * prop).
 *
 * Scenarios:
 *   1. Controlled-input contract: rendered `search` prop is reflected in
 *      the input value; typing fires `onSearchChange` per keystroke.
 *   2. Empty `search` prop → input is empty; placeholder visible.
 *   3. `onCreateSkuClick` button only renders when handler provided.
 *   4. Bilingual: locale switch flips the placeholder copy.
 *   5. URL-state seam: changing the value triggers `onSearchChange` with
 *      the substring, demonstrating the seam the route uses to write to
 *      `?search=` via `useFilterSearchParams`.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FilterStrip } from './FilterStrip';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

beforeEach(() => {
  __resetLocaleForTests();
});

afterEach(() => {
  __resetLocaleForTests();
});

describe('FilterStrip', () => {
  it('reflects the controlled `search` prop in the input value', () => {
    render(<FilterStrip search="YEN-001" onSearchChange={() => {}} />);
    const input = screen.getByLabelText('Tìm SKU') as HTMLInputElement;
    expect(input.value).toBe('YEN-001');
  });

  it('renders an empty input when `search` is the empty string', () => {
    render(<FilterStrip search="" onSearchChange={() => {}} />);
    const input = screen.getByLabelText('Tìm SKU') as HTMLInputElement;
    expect(input.value).toBe('');
  });

  it('fires onSearchChange per keystroke as the user types', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<FilterStrip search="" onSearchChange={onChange} />);

    const input = screen.getByLabelText('Tìm SKU');
    await user.type(input, 'abc');

    // userEvent.type fires onChange once per character.
    expect(onChange).toHaveBeenCalledTimes(3);
    expect(onChange).toHaveBeenLastCalledWith('c');
  });

  it('renders the "New SKU" button only when onCreateSkuClick is provided', () => {
    const { rerender } = render(
      <FilterStrip search="" onSearchChange={() => {}} />,
    );
    expect(screen.queryByText('Thêm SKU')).not.toBeInTheDocument();

    rerender(
      <FilterStrip search="" onSearchChange={() => {}} onCreateSkuClick={() => {}} />,
    );
    expect(screen.getByText('Thêm SKU')).toBeInTheDocument();
  });

  it('triggers onCreateSkuClick when the button is activated', async () => {
    const onCreate = vi.fn();
    const user = userEvent.setup();
    render(
      <FilterStrip search="" onSearchChange={() => {}} onCreateSkuClick={onCreate} />,
    );

    await user.click(screen.getByText('Thêm SKU'));
    expect(onCreate).toHaveBeenCalledTimes(1);
  });

  it('switches to English copy when locale flips', () => {
    setLang('en');
    render(<FilterStrip search="" onSearchChange={() => {}} />);
    expect(screen.getByLabelText('Search SKU')).toBeInTheDocument();
  });

  it('URL-state seam: typing emits the substring upstream (consumed by useFilterSearchParams)', async () => {
    // This mirrors the integration with `routes/_auth/inventory.tsx`, which
    // wires `onSearchChange` to `setSearch({ search: value || undefined })`.
    // The strip itself doesn't touch the URL — that's the route's job — but
    // this seam test pins the contract that the route depends on.
    const captured: string[] = [];
    const user = userEvent.setup();
    render(
      <FilterStrip
        search=""
        onSearchChange={(value) => {
          captured.push(value);
        }}
      />,
    );

    await user.type(screen.getByLabelText('Tìm SKU'), 'YS');
    expect(captured).toEqual(['Y', 'S']);
  });
});
