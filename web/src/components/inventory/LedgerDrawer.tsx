/**
 * LedgerDrawer — Sprint-6 plan U10; Sprint-7.5 U7 URL-state migration.
 *
 * Composes the reusable Drawer primitive with:
 *   - <AllocationBar> header (per-channel split — renders placeholder
 *     until Sprint-7 backfills the cross-module join, per trade-off #3).
 *   - <table.t-data> body of <LedgerRow> entries.
 *
 * Open / close contract (Sprint-7.5 U7 — URL-driven):
 *   The drawer is open when `selectedSku` is non-null. The parent route
 *   derives `selectedSku` from the `?selected=` URL param; close emits
 *   `setSearch({ selected: undefined, ledger: undefined })` which the
 *   parent wires to `onClose`. `item` is looked up from the SKU list and
 *   may be `null` when the deep-linked SKU doesn't exist in the loaded
 *   list (stale-deep-link case — see D-005 below).
 *
 * Stale deep-link recovery (post-doc-review D-005):
 *   When `selectedSku` is set but `item` is null AND `isUnknownSelectedSku`
 *   is true, the drawer renders an explicit error state ("This SKU could
 *   not be found …") with a Close button. The URL param is NOT cleared
 *   automatically so a refresh preserves the error context.
 *
 * Ledger cursor (Sprint-7.5 U6 seam):
 *   `ledgerCursor` flows from the URL via `?ledger=`. U6 (next round)
 *   wires the "Load more" consumer that reads + advances this cursor.
 *   For the U7 commit the prop is plumbed end-to-end but not consumed by
 *   `useSkuLedgerQuery` yet — leaving a clean drop-in point for U6.
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
import { FlashSaleToggle } from './FlashSaleToggle';
import { useSkuLedgerQuery } from '../../hooks/useInventoryQuery';
import { t, useLocale } from '../../hooks/useLocale';

export interface LedgerDrawerProps {
  item: SkuListItem | null;
  /**
   * SKU from `?selected=` URL param. Drives the drawer's open state and the
   * D-005 error-state rendering when `item` resolves to null. Backwards-
   * compatible: callers that supply only `item` (e.g., existing Sprint-6
   * tests) still get the original "open iff item !== null" behaviour.
   */
  selectedSku?: string | null;
  /**
   * Set to true when the parent route has confirmed the loaded SKU list
   * does NOT contain `selectedSku` (D-005 stale-deep-link case). Triggers
   * the explicit error-state copy. Defaults to false.
   */
  isUnknownSelectedSku?: boolean;
  /**
   * Ledger pagination cursor from `?ledger=` URL param (Sprint-7.5 U6
   * seam). U6 will wire this into `useSkuLedgerQuery`; for U7 the prop is
   * accepted but unused so the seam is established without touching the
   * data-layer call.
   */
  ledgerCursor?: string | null;
  onClose: () => void;
  /** Click handler for the "Điều chỉnh tồn" CTA (U11). Receives the SKU. */
  onAdjustClick?: (sku: string) => void;
}

export function LedgerDrawer({
  item,
  selectedSku,
  isUnknownSelectedSku,
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- U6 seam
  ledgerCursor,
  onClose,
  onAdjustClick,
}: LedgerDrawerProps) {
  useLocale();
  // The fetch key is the loaded item's SKU OR the URL-selected SKU. The
  // latter triggers the network request even when the SKU isn't in the
  // current page of the list, so the error state can show the real
  // "SKU not found" diagnosis rather than a generic empty drawer.
  // U6 will read `ledgerCursor` here when it lands.
  const sku = item?.sku ?? selectedSku ?? null;
  const { data, isLoading, isError } = useSkuLedgerQuery(sku);

  // Open semantics: backwards-compatible with the Sprint-6 contract
  // (open iff `item !== null`) PLUS the new URL-driven contract (open iff
  // `selectedSku` is set, even when `item` resolves to null for the
  // stale-deep-link case).
  const isOpen = item !== null || (selectedSku != null && selectedSku.length > 0);
  const displayedSku = item?.sku ?? selectedSku ?? '';
  const title = isOpen
    ? `${t('Sổ giữ chỗ', 'Reservation ledger')} · ${displayedSku}`
    : '';
  const headerExtra = item ? (
    <FlashSaleToggle sku={item.sku} value={item.isFlashSale} />
  ) : undefined;

  return (
    <Drawer isOpen={isOpen} onClose={onClose} title={title} headerExtra={headerExtra}>
      {isUnknownSelectedSku && !item ? (
        <UnknownSkuState sku={selectedSku ?? ''} onClose={onClose} />
      ) : isOpen ? (
        <div style={{ padding: 'var(--s-4) var(--s-5)' }}>
          {item ? (
            <section style={{ marginBottom: 'var(--s-5)' }}>
              <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
                {t('Phân bổ kênh', 'Channel allocation')}
              </div>
              <AllocationBar allocations={item.allocations} />
            </section>
          ) : null}
          {onAdjustClick && item ? (
            <section
              style={{
                marginBottom: 'var(--s-5)',
                display: 'flex',
                justifyContent: 'flex-end',
              }}
            >
              <button
                type="button"
                className="btn primary"
                onClick={() => onAdjustClick(item.sku)}
                data-testid="ledger-adjust-cta"
              >
                {t('Điều chỉnh tồn', 'Adjust stock')}
              </button>
            </section>
          ) : null}
          <section>
            <div className="lbl" style={{ marginBottom: 'var(--s-2)' }}>
              {t('Lịch sử giao dịch', 'Ledger entries')}
            </div>
            <LedgerBody
              data={data?.items ?? []}
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
            <LedgerRow key={entry.id} entry={entry} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

interface UnknownSkuStateProps {
  sku: string;
  onClose: () => void;
}

/**
 * Stale-deep-link recovery (Sprint-7.5 U7 / D-005).
 *
 * Renders when `?selected=` points at a SKU that does not exist in the
 * loaded list. The URL param is intentionally NOT cleared (the explicit
 * Close button puts the user in control); refresh preserves the error
 * context.
 */
function UnknownSkuState({ sku, onClose }: UnknownSkuStateProps) {
  return (
    <div
      role="alert"
      data-testid="ledger-unknown-sku"
      style={{ padding: 'var(--s-5)', color: 'var(--ink-1)' }}
    >
      <div className="t-lg" style={{ fontWeight: 600, marginBottom: 'var(--s-2)' }}>
        {t('Không tìm thấy SKU', 'SKU not found')}
      </div>
      <p className="t-sm" style={{ color: 'var(--ink-2)', marginBottom: 'var(--s-4)' }}>
        {t(
          `Không tìm thấy SKU "${sku}" — có thể nó đã bị xoá hoặc liên kết đã hỏng.`,
          `This SKU ("${sku}") could not be found — it may have been deleted or the link is invalid.`,
        )}
      </p>
      <button
        type="button"
        className="btn"
        onClick={onClose}
        data-testid="ledger-unknown-sku-close"
      >
        {t('Đóng', 'Close')}
      </button>
    </div>
  );
}
