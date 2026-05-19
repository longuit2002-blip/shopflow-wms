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

export interface SkuTableProps {
  items: SkuListItem[];
  onSelectSku: (sku: string) => void;
  selectedSku?: string | null;
  isLoading?: boolean;
}

export function SkuTable({ items, onSelectSku, selectedSku, isLoading }: SkuTableProps) {
  const { lang } = useLocale();

  if (items.length === 0 && !isLoading) {
    return <SkuTableEmpty />;
  }

  return (
    <div style={{ overflow: 'auto', flex: 1 }}>
      <table className="t-data">
        <thead>
          <tr>
            <th scope="col">{t('SKU', 'SKU')}</th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Tồn thực', 'Available')}
            </th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Đã giữ', 'Reserved')}
            </th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Mức an toàn', 'Threshold')}
            </th>
            <th scope="col">{t('Trạng thái', 'Status')}</th>
          </tr>
        </thead>
        <tbody>
          {items.map((row) => {
            const isSelected = selectedSku === row.Sku;
            const isBelow = row.Threshold != null && row.Available < row.Threshold;
            const isRisk = row.Reserved > row.Available;
            return (
              <tr
                key={row.Sku}
                className={isSelected ? 'sel' : undefined}
                onClick={() => onSelectSku(row.Sku)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onSelectSku(row.Sku);
                  }
                }}
                role="button"
                tabIndex={0}
                aria-pressed={isSelected}
                style={{ cursor: 'pointer' }}
              >
                <td>
                  <span className="mono">{row.Sku}</span>
                  {row.IsFlashSale && (
                    <Pill kind="accent" style={{ marginLeft: 6 }}>
                      {t('Flash', 'Flash')}
                    </Pill>
                  )}
                </td>
                <td className="num" style={{ textAlign: 'right' }}>
                  {fmtNum(row.Available, lang)}
                </td>
                <td className="num" style={{ textAlign: 'right' }}>
                  {fmtNum(row.Reserved, lang)}
                </td>
                <td
                  className="num"
                  style={{ textAlign: 'right' }}
                  // The cell hosts an interactive editor; clicks on the
                  // editor must not bubble up and open the ledger drawer.
                  onClick={(e) => e.stopPropagation()}
                  onKeyDown={(e) => e.stopPropagation()}
                >
                  <ThresholdInlineEdit sku={row.Sku} value={row.Threshold} />
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
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
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
