/**
 * FlashSaleToggle — Sprint-6 plan U12 (R10).
 *
 * Wraps the Toggle primitive with the Inventory `setFlashSale` mutation.
 * Lives in the LedgerDrawer header so Owner can flip the per-SKU flash-
 * sale flag without leaving the drawer.
 *
 * Optimistic UI: the toggle visual flips immediately on click; the
 * mutation runs in the background. On failure (5xx or 4xx), the
 * useInventoryMutations hook pushes an error toast and the optimistic
 * value reverts to the server `value`. The SKU table's 2-s poll
 * confirms the new state on success.
 *
 * Anti-double-click: the toggle is disabled while the mutation is
 * pending — covers the plan's "rapid clicks fire 1 effective request"
 * scenario without a debounce hook.
 *
 * Sprint-6 trade-off — the mutation hits Inventory.Api's
 * `PUT /api/v1/inventory/skus/{sku}/flash-sale` (which updates the
 * in-memory display flag the SKU list reads). The plan suggested
 * StockSync's `PUT /api/v1/skus/{sku}/flag` for cross-module routing,
 * but Sprint-6 has no cross-module sync (trade-off #3); Sprint-7
 * routes the toggle through both surfaces when persistence lands.
 */

import { useState } from 'react';
import { Toggle } from '../primitives/Toggle';
import { useInventoryMutations } from '../../hooks/useInventoryMutations';
import { t, useLocale } from '../../hooks/useLocale';

export interface FlashSaleToggleProps {
  sku: string;
  value: boolean;
}

export function FlashSaleToggle({ sku, value }: FlashSaleToggleProps) {
  useLocale();
  const { setFlashSale } = useInventoryMutations();
  const [optimisticValue, setOptimisticValue] = useState(value);
  const [lastServerValue, setLastServerValue] = useState(value);

  // React 19 "Adjusting State Based on Props" pattern — when the polled
  // SKU list reports a new server value, sync the optimistic state.
  // Also catches the post-error revert path (the mutation's catch sets
  // optimisticValue back to value, but if the parent re-renders with a
  // changed value first, this branch handles it).
  if (lastServerValue !== value) {
    setLastServerValue(value);
    setOptimisticValue(value);
  }

  async function handleChange(next: boolean): Promise<void> {
    setOptimisticValue(next);
    try {
      await setFlashSale.mutateAsync({ sku, active: next });
    } catch {
      setOptimisticValue(value);
    }
  }

  return (
    <Toggle
      checked={optimisticValue}
      onChange={(next) => void handleChange(next)}
      label={t('Flash-sale', 'Flash-sale')}
      disabled={setFlashSale.isPending}
      data-testid={`flash-toggle-${sku}`}
    />
  );
}
