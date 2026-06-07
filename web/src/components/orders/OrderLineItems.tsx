/**
 * OrderLineItems — Sprint-7 plan U13.
 *
 * Compact table of order lines for the Orders detail route. Each row
 * shows: SKU (mono), Qty (right-aligned numeric), ExpectedWeight (right-
 * aligned numeric or em-dash when null), and a cell-level "View ledger"
 * button that drills into the Sprint-6 reservation-ledger drawer for that
 * SKU.
 *
 * KTD11 — nested-interactive a11y. The row itself is NOT a button (no
 * role/onClick on the <tr>); the action-cell hosts the real <button>.
 * This mirrors Sprint-6 SkuTable exactly: the cell-level button avoids
 * the axe `nested-interactive` violation that would arise from wrapping
 * a row click around interactive descendants.
 *
 * Wire shape: camelCase fields (`OrderLineResponse` from `api/orders.ts`)
 * — Sprint-7.5 U1/U2 wire normalisation.
 *
 * Pure presentation: no data fetching, no router coupling. The parent
 * route holds the open-ledger state and renders the LedgerDrawer.
 */

import type { OrderLineResponse } from '../../api/orders';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

export interface OrderLineItemsProps {
  lines: OrderLineResponse[];
  onLineClick: (line: OrderLineResponse) => void;
  isLoading?: boolean;
}

export function OrderLineItems({ lines, onLineClick, isLoading }: OrderLineItemsProps) {
  const { lang } = useLocale();

  if (isLoading) {
    return (
      <div
        data-testid="order-lines-loading"
        style={{ padding: 'var(--s-4)', color: 'var(--ink-2)' }}
      >
        {t('Đang tải', 'Loading')}
      </div>
    );
  }

  if (lines.length === 0) {
    return (
      <div
        data-testid="order-lines-empty"
        style={{ padding: 'var(--s-4)', color: 'var(--ink-2)' }}
      >
        {t('Chưa có dòng đơn', 'No line items')}
      </div>
    );
  }

  return (
    <div style={{ overflowX: 'auto' }} data-testid="order-lines">
      <table className="t-data">
        <thead>
          <tr>
            <th scope="col">{t('SKU', 'SKU')}</th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Số lượng', 'Qty')}
            </th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Khối lượng dự kiến', 'Expected weight')}
            </th>
            <th scope="col" aria-label={t('Hành động', 'Action')} />
          </tr>
        </thead>
        <tbody>
          {lines.map((line) => (
            <tr key={line.id} data-testid={`order-line-${line.sku}`}>
              <td
                style={{
                  fontFamily: 'var(--font-mono)',
                }}
              >
                {line.sku}
              </td>
              <td className="num" style={{ textAlign: 'right' }}>
                {fmtNum(line.qty, lang)}
              </td>
              <td className="num" style={{ textAlign: 'right' }}>
                {line.expectedWeight !== null
                  ? `${fmtNum(line.expectedWeight, lang)} g`
                  : '—'}
              </td>
              <td style={{ textAlign: 'right' }}>
                <button
                  type="button"
                  className="btn"
                  onClick={() => onLineClick(line)}
                  data-testid={`order-line-view-ledger-${line.sku}`}
                  aria-label={`${t('Xem sổ giữ chỗ cho', 'View reservation ledger for')} ${line.sku}`}
                >
                  {t('Xem ledger', 'View ledger')}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
