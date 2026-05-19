import { useCallback, useMemo, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { useInventoryQuery } from '../../hooks/useInventoryQuery';
import { KpiStrip } from '../../components/inventory/KpiStrip';
import { FilterStrip } from '../../components/inventory/FilterStrip';
import { SkuTable } from '../../components/inventory/SkuTable';
import { LedgerDrawer } from '../../components/inventory/LedgerDrawer';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Inventory route — Sprint-6 vertical-slice landing.
 *
 * Assembles: KPI strip + filter strip + SKU table. Drawer mounts in U10;
 * Adjust modal + Create-SKU modal mount in U11/U12. Search is debounced
 * implicitly by TanStack Query keying on the filter state.
 */
export const Route = createFileRoute('/_auth/inventory')({
  component: InventoryRouteComponent,
});

function InventoryRouteComponent() {
  useLocale();
  const [search, setSearch] = useState('');
  const [selectedSku, setSelectedSku] = useState<string | null>(null);

  const params = useMemo(() => ({ search: search || undefined, pageSize: 100 }), [search]);
  const { data, isLoading, isError } = useInventoryQuery(params);

  const selectedItem = useMemo(
    () => (selectedSku ? (data?.Items ?? []).find((i) => i.Sku === selectedSku) ?? null : null),
    [data, selectedSku],
  );
  const closeDrawer = useCallback(() => setSelectedSku(null), []);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      <KpiStrip />
      <FilterStrip search={search} onSearchChange={setSearch} />
      {isError ? (
        <ErrorState />
      ) : (
        <SkuTable
          items={data?.Items ?? []}
          onSelectSku={setSelectedSku}
          selectedSku={selectedSku}
          isLoading={isLoading}
        />
      )}
      <LedgerDrawer item={selectedItem} onClose={closeDrawer} />
    </div>
  );
}

function ErrorState() {
  return (
    <div
      role="alert"
      style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--s-8)',
        color: 'var(--bad-ink)',
      }}
    >
      <div className="t-lg" style={{ fontWeight: 600 }}>
        {t('Không tải được dữ liệu tồn kho', 'Could not load inventory data')}
      </div>
      <div className="t-sm" style={{ marginTop: 'var(--s-2)', color: 'var(--ink-2)' }}>
        {t(
          'Backend đang sẽ thử lại tự động sau vài giây.',
          'The backend will retry automatically in a few seconds.',
        )}
      </div>
    </div>
  );
}
