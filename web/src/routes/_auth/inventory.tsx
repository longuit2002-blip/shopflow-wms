import { createFileRoute } from '@tanstack/react-router';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Inventory route — Sprint-6 vertical slice landing.
 *
 * Renders a placeholder header until U9 ships the SKU table + filter
 * strip + KPI strip + 2 s polling.
 */
export const Route = createFileRoute('/_auth/inventory')({
  component: InventoryRouteComponent,
});

function InventoryRouteComponent() {
  useLocale();
  return (
    <div style={{ padding: 'var(--s-6)', flex: 1 }}>
      <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
        {t('Tồn kho', 'Inventory')}
      </h1>
      <p className="t-sm" style={{ color: 'var(--ink-2)', marginTop: 'var(--s-2)' }}>
        {t(
          'SKU table, filter strip, KPI strip và reservation ledger drawer sẽ được wire trong U9 + U10.',
          'SKU table, filter strip, KPI strip, and reservation ledger drawer land in U9 + U10.',
        )}
      </p>
    </div>
  );
}
