/**
 * SKU table — Sprint-6 plan U9 / R4.
 *
 * Renders the live SKU list with available, reserved, threshold, and
 * flash-sale flag. Click a row → opens the reservation ledger drawer
 * (U10). Sticky header per design canon `.t-data thead`.
 *
 * Columns:
 *   SKU | Available | Reserved | Threshold | Flash sale | (actions)
 *
 * Sprint-7 adds: name, category, channel allocation chips, zone, p24
 * outbound. Sprint-6 ships a tighter table to match the actual data
 * available from /api/v1/inventory/skus.
 */

import type { SkuListItem } from '../../api/inventory';
import { Pill } from '../primitives/Pill';
import { ThresholdInlineEdit } from './ThresholdInlineEdit';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

/** Sortable columns exposed in the URL schema (Sprint-7.5 U7). */
export type SortColumn = 'sku' | 'available' | 'reserved';
export type SortDirection = 'asc' | 'desc';

export interface SkuTableProps {
  items: SkuListItem[];
  onSelectSku: (sku: string) => void;
  selectedSku?: string | null;
  isLoading?: boolean;
  /** Active sort column (Sprint-7.5 U7); undefined = API order (default). */
  sortColumn?: SortColumn;
  /** Active sort direction (Sprint-7.5 U7); undefined = neutral. */
  sortDirection?: SortDirection;
  /** Called when a sortable column header is activated (Sprint-7.5 U7). */
  onSortChange?: (column: SortColumn) => void;
  /**
   * Sprint-7.5 U4 — called when the row's Edit button is clicked.
   * When omitted the Edit cell is suppressed (preserves existing tests +
   * read-only consumers).
   */
  onEditClick?: (sku: string) => void;
}

export function SkuTable({
  items,
  onSelectSku,
  selectedSku,
  isLoading,
  sortColumn,
  sortDirection,
  onSortChange,
  onEditClick,
}: SkuTableProps) {
  const { lang } = useLocale();

  if (items.length === 0 && !isLoading) {
    return <SkuTableEmpty />;
  }

  return (
    <div style={{ overflow: 'auto', flex: 1 }}>
      <table className="t-data">
        <thead>
          <tr>
            <SortableHeader
              column="sku"
              label={t('SKU', 'SKU')}
              align="left"
              activeColumn={sortColumn}
              activeDirection={sortDirection}
              onSortChange={onSortChange}
            />
            <SortableHeader
              column="available"
              label={t('Tồn thực', 'Available')}
              align="right"
              activeColumn={sortColumn}
              activeDirection={sortDirection}
              onSortChange={onSortChange}
            />
            <SortableHeader
              column="reserved"
              label={t('Đã giữ', 'Reserved')}
              align="right"
              activeColumn={sortColumn}
              activeDirection={sortDirection}
              onSortChange={onSortChange}
            />
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Mức an toàn', 'Threshold')}
            </th>
            <th scope="col">{t('Trạng thái', 'Status')}</th>
            {onEditClick ? (
              <th scope="col" aria-label={t('Thao tác', 'Actions')}></th>
            ) : null}
          </tr>
        </thead>
        <tbody>
          {items.map((row) => {
            const isSelected = selectedSku === row.sku;
            const isBelow = row.threshold != null && row.available < row.threshold;
            const isRisk = row.reserved > row.available;
            return (
              <tr
                key={row.sku}
                className={isSelected ? 'sel' : undefined}
                onClick={() => onSelectSku(row.sku)}
                style={{ cursor: 'pointer' }}
              >
                {/*
                  Mouse-anywhere-in-row → drawer is a convenience; the
                  semantic focus target is the SKU button in the first
                  cell. Row itself is NOT role=button (otherwise the
                  threshold-cell button below nests two interactives,
                  per axe `nested-interactive`).
                */}
                <td>
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      onSelectSku(row.sku);
                    }}
                    aria-pressed={isSelected}
                    aria-label={`${t('Mở sổ giữ chỗ cho', 'Open reservation ledger for')} ${row.sku}`}
                    className="row-link"
                    data-testid={`sku-row-${row.sku}`}
                    style={{
                      all: 'unset',
                      cursor: 'pointer',
                      fontFamily: 'var(--font-mono)',
                      color: 'inherit',
                    }}
                  >
                    {row.sku}
                  </button>
                  {row.isFlashSale && (
                    <Pill kind="accent" style={{ marginLeft: 6 }}>
                      {t('Flash', 'Flash')}
                    </Pill>
                  )}
                </td>
                <td className="num" style={{ textAlign: 'right' }}>
                  {fmtNum(row.available, lang)}
                </td>
                <td className="num" style={{ textAlign: 'right' }}>
                  {fmtNum(row.reserved, lang)}
                </td>
                <td
                  className="num"
                  style={{ textAlign: 'right' }}
                  // The cell hosts an interactive editor; clicks on the
                  // editor must not bubble up and open the ledger drawer.
                  onClick={(e) => e.stopPropagation()}
                  onKeyDown={(e) => e.stopPropagation()}
                >
                  <ThresholdInlineEdit sku={row.sku} value={row.threshold} />
                </td>
                <td>
                  {isRisk ? (
                    <Pill kind="bad">{t('Nguy cơ', 'Oversell risk')}</Pill>
                  ) : isBelow ? (
                    <Pill kind="warn">{t('Dưới mức', 'Below threshold')}</Pill>
                  ) : (
                    <Pill kind="ok">{t('Ổn định', 'OK')}</Pill>
                  )}
                </td>
                {onEditClick ? (
                  <td
                    onClick={(e) => e.stopPropagation()}
                    onKeyDown={(e) => e.stopPropagation()}
                  >
                    <button
                      type="button"
                      className="btn ghost"
                      onClick={() => onEditClick(row.sku)}
                      data-testid={`sku-edit-${row.sku}`}
                      aria-label={`${t('Chỉnh sửa SKU', 'Edit SKU')} ${row.sku}`}
                    >
                      {t('Sửa', 'Edit')}
                    </button>
                  </td>
                ) : null}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

interface SortableHeaderProps {
  column: SortColumn;
  label: string;
  align: 'left' | 'right';
  activeColumn: SortColumn | undefined;
  activeDirection: SortDirection | undefined;
  onSortChange?: (column: SortColumn) => void;
}

/**
 * Column header with an embedded sort-toggle button (Sprint-7.5 U7).
 *
 * Click cycle: neutral → asc → desc → neutral. When `onSortChange` is not
 * provided the header degrades to a static `<th>` (no nested-interactive
 * regression — see Sprint-6 KTD11). aria-sort drives screen readers.
 */
function SortableHeader({
  column,
  label,
  align,
  activeColumn,
  activeDirection,
  onSortChange,
}: SortableHeaderProps) {
  const isActive = activeColumn === column;
  const ariaSort: 'ascending' | 'descending' | 'none' = isActive
    ? activeDirection === 'desc'
      ? 'descending'
      : 'ascending'
    : 'none';

  if (!onSortChange) {
    return (
      <th scope="col" style={{ textAlign: align }}>
        {label}
      </th>
    );
  }

  const indicator = isActive ? (activeDirection === 'desc' ? ' ↓' : ' ↑') : '';

  return (
    <th scope="col" aria-sort={ariaSort} style={{ textAlign: align }}>
      <button
        type="button"
        onClick={() => onSortChange(column)}
        data-testid={`sku-sort-${column}`}
        aria-label={`${label} — sort`}
        style={{
          all: 'unset',
          cursor: 'pointer',
          fontWeight: 'inherit',
          color: 'inherit',
        }}
      >
        {label}
        {indicator}
      </button>
    </th>
  );
}

function SkuTableEmpty() {
  return (
    <div
      style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--s-8)',
        color: 'var(--ink-3)',
      }}
    >
      <div className="t-lg" style={{ fontWeight: 600, color: 'var(--ink-2)' }}>
        {t('Chưa có SKU nào', 'No SKUs yet')}
      </div>
      <div className="t-sm" style={{ marginTop: 'var(--s-2)' }}>
        {t(
          'Bấm "Thêm SKU" ở thanh lọc để tạo SKU đầu tiên.',
          'Click "New SKU" in the filter strip to create your first SKU.',
        )}
      </div>
    </div>
  );
}
