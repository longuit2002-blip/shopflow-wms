/**
 * LedgerDrawer — Sprint-6 plan U10.
 *
 * Composes the reusable Drawer primitive with:
 *   - <AllocationBar> header (per-channel split — renders placeholder
 *     until Sprint-7 backfills the cross-module join, per trade-off #3).
 *   - <table.t-data> body of <LedgerRow> entries.
 *
 * Open / close contract: the drawer is open when `item` is non-null. The
 * parent route holds the `selectedSku` state and looks the matching
 * SkuListItem up from its inventory query result (this lets the drawer
 * read Allocations without re-fetching).
 *
 * Live-sync: `useSkuLedgerQuery` does NOT poll (the SKU table's 2-s
 * inventory poll is sufficient for the table; U11's mutations explicitly
 * invalidate the ledger query key on success; Sprint-7 swaps in SignalR
 * push so the drawer also updates push-side without a poll).
 */

import type { SkuLedgerEntry, SkuListItem } from '../../api/inventory';
import { Drawer } from '../primitives/Drawer';
import { AllocationBar } from './AllocationBar';
import { LedgerRow } from './LedgerRow';
import { useSkuLedgerQuery } from '../../hooks/useInventoryQuery';
import { t, useLocale } from '../../hooks/useLocale';

export interface LedgerDrawerProps {
  item: SkuListItem | null;
  onClose: () => void;
}

export function LedgerDrawer({ item, onClose }: LedgerDrawerProps) {
  useLocale();
  const sku = item?.Sku ?? null;
  const { data, isLoading, isError } = useSkuLedgerQuery(sku);

  const isOpen = item !== null;
  const title = item ? `${t('Sổ giữ chỗ', 'Reservation ledger')} · ${item.Sku}` : '';

  return (
    <Drawer isOpen={isOpen} onClose={onClose} title={title}>
      {item ? (
        <div style={{ padding: 'var(--s-4) var(--s-5)' }}>
          <section style={{ marginBottom: 'var(--s-5)' }}>
            <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
              {t('Phân bổ kênh', 'Channel allocation')}
            </div>
            <AllocationBar allocations={item.Allocations} />
          </section>
          <section>
            <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
              {t('Lịch sử giao dịch', 'Ledger entries')}
            </div>
            <LedgerBody
              data={data?.Items ?? []}
              isLoading={isLoading}
              isError={isError}
            />
          </section>
        </div>
      ) : null}
    </Drawer>
  );
}

interface LedgerBodyProps {
  data: SkuLedgerEntry[];
  isLoading: boolean;
  isError: boolean;
}

function LedgerBody({ data, isLoading, isError }: LedgerBodyProps) {
  if (isError) {
    return (
      <div
        role="alert"
        data-testid="ledger-error"
        style={{ padding: 'var(--s-4)', color: 'var(--bad-ink)' }}
      >
        {t('Không tải được sổ giữ chỗ.', 'Could not load the ledger.')}
      </div>
    );
  }
  if (isLoading) {
    return (
      <div
        data-testid="ledger-loading"
        style={{ padding: 'var(--s-4)', color: 'var(--ink-2)' }}
      >
        {t('Đang tải…', 'Loading…')}
      </div>
    );
  }
  if (data.length === 0) {
    return (
      <div
        data-testid="ledger-empty"
        style={{ padding: 'var(--s-4)', color: 'var(--ink-2)' }}
      >
        {t('Chưa có giao dịch nào', 'No transactions yet')}
      </div>
    );
  }
  return (
    <div style={{ overflowX: 'auto' }}>
      <table className="t-data">
        <thead>
          <tr>
            <th scope="col">{t('Thời gian', 'Time')}</th>
            <th scope="col">{t('Đơn / Dòng', 'Order / Line')}</th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Số lượng', 'Qty')}
            </th>
            <th scope="col">{t('Trạng thái', 'Status')}</th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Tồn còn lại', 'Running balance')}
            </th>
          </tr>
        </thead>
        <tbody>
          {data.map((entry) => (
            <LedgerRow key={entry.Id} entry={entry} />
          ))}
        </tbody>
      </table>
    </div>
  );
}
