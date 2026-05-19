/**
 * OrderLineItems tests — Sprint-7 plan U13.
 *
 * Scenarios:
 *   1. 3-line response renders 3 rows with SKU + Qty + Weight.
 *   2. Click action button → onLineClick called with that line's data.
 *   3. KTD11 nested-interactive check: the row element is NOT a button;
 *      the action cell hosts the real button.
 *   4. Empty `lines` → empty-state copy.
 *   5. A11y: vitest-axe → no violations.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import { OrderLineItems } from './OrderLineItems';
import type { OrderLineResponse } from '../../api/orders';
import { __resetLocaleForTests } from '../../hooks/useLocale';

beforeEach(() => {
  __resetLocaleForTests();
});

afterEach(() => {
  __resetLocaleForTests();
});

const THREE_LINES: OrderLineResponse[] = [
  {
    Id: '01HOLINE0000000000000001',
    Sku: 'SKU-RED-001',
    Qty: 2,
    ExpectedWeight: 350,
  },
  {
    Id: '01HOLINE0000000000000002',
    Sku: 'SKU-BLU-002',
    Qty: 5,
    ExpectedWeight: 120,
  },
  {
    Id: '01HOLINE0000000000000003',
    Sku: 'SKU-GRN-003',
    Qty: 1,
    ExpectedWeight: null,
  },
];

describe('OrderLineItems', () => {
  it('renders 3 rows with SKU, Qty, and Weight cells', () => {
    render(<OrderLineItems lines={THREE_LINES} onLineClick={() => {}} />);

    expect(screen.getByTestId('order-line-SKU-RED-001')).toBeInTheDocument();
    expect(screen.getByTestId('order-line-SKU-BLU-002')).toBeInTheDocument();
    expect(screen.getByTestId('order-line-SKU-GRN-003')).toBeInTheDocument();

    const redRow = screen.getByTestId('order-line-SKU-RED-001');
    expect(within(redRow).getByText('SKU-RED-001')).toBeInTheDocument();
    // Vietnamese-locale (default) formats 2 verbatim, 350 with no thousands separator.
    expect(within(redRow).getByText('2')).toBeInTheDocument();
    expect(within(redRow).getByText(/350.*g/)).toBeInTheDocument();

    // Null-weight row falls back to em-dash.
    const greenRow = screen.getByTestId('order-line-SKU-GRN-003');
    expect(within(greenRow).getByText('—')).toBeInTheDocument();
  });

  it('invokes onLineClick with the clicked line when the action button is pressed', async () => {
    const user = userEvent.setup();
    const onLineClick = vi.fn();
    render(<OrderLineItems lines={THREE_LINES} onLineClick={onLineClick} />);

    const blueButton = screen.getByTestId('order-line-view-ledger-SKU-BLU-002');
    await user.click(blueButton);

    expect(onLineClick).toHaveBeenCalledTimes(1);
    expect(onLineClick).toHaveBeenCalledWith(THREE_LINES[1]);
  });

  it('KTD11 — the row element is NOT a button; the action cell hosts the button', () => {
    render(<OrderLineItems lines={THREE_LINES} onLineClick={() => {}} />);

    const redRow = screen.getByTestId('order-line-SKU-RED-001');
    // The <tr> must NOT carry button semantics that would trip
    // axe `nested-interactive` against its descendant action button.
    expect(redRow.tagName).toBe('TR');
    expect(redRow).not.toHaveAttribute('role', 'button');
    expect(redRow).not.toHaveAttribute('onclick');

    // The action button lives inside the row's action cell, not on the row itself.
    const button = within(redRow).getByTestId('order-line-view-ledger-SKU-RED-001');
    expect(button.tagName).toBe('BUTTON');
  });

  it('renders the empty-state copy when lines is empty', () => {
    render(<OrderLineItems lines={[]} onLineClick={() => {}} />);
    expect(screen.getByTestId('order-lines-empty')).toHaveTextContent('Chưa có dòng đơn');
  });

  it('container is axe-clean', async () => {
    const { container } = render(
      <OrderLineItems lines={THREE_LINES} onLineClick={() => {}} />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });
});
