import { useCallback, useMemo, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { useInventoryQuery } from '../../hooks/useInventoryQuery';
import { useFilterSearchParams } from '../../hooks/useFilterSearchParams';
import { KpiStrip } from '../../components/inventory/KpiStrip';
import { FilterStrip } from '../../components/inventory/FilterStrip';
import {
  SkuTable,
  type SortColumn,
  type SortDirection,
} from '../../components/inventory/SkuTable';
import { LedgerDrawer } from '../../components/inventory/LedgerDrawer';
import { AdjustStockModal } from '../../components/inventory/AdjustStockModal';
import { CreateSkuModal } from '../../components/inventory/CreateSkuModal';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Inventory route — Sprint-6 vertical-slice landing; Sprint-7.5 U7 URL-state
 * migration.
 *
 * URL-search-params shape (per KTD5 — `useFilterSearchParams` adoption):
 *   ?search=…       free-text SKU search (substring match server-side)
 *   ?sort=sku|availableAsc|availableDesc|reservedAsc|reservedDesc
 *   ?page=2,3,…     pagination (1 = default, omitted from URL)
 *   ?selected=SKU-001
 *                   drawer-open state (LedgerDrawer mounts when present)
 *   ?ledger=cursor  reservation-ledger pagination cursor (U6 consumer wires
 *                   the "Load more" button on top of this seam without
 *                   touching the helper)
 *
 * Reload preserves all of it; deep-links open the same view; back-button
 * traverses filter changes (every navigate is replace:false per the helper).
 *
 * Filter/sort change auto-resets page + clears ledger cursor (D-006 rule
 * enforced inside the helper; this route declares the trigger keys).
 *
 * Adjust + Create modals stay in local React state — they are transient
 * action dialogs, not addressable URL states.
 */
export const Route = createFileRoute('/_auth/inventory')({
  validateSearch: (raw: Record<string, unknown>): InventorySearch => {
    return {
      search:
        typeof raw.search === 'string' && raw.search.length > 0 ? raw.search : undefined,
      sort: isSortColumn(raw.sort) ? raw.sort : undefined,
      sortDir: isSortDirection(raw.sortDir) ? raw.sortDir : undefined,
      page: toPositiveInt(raw.page) ?? undefined,
      selected:
        typeof raw.selected === 'string' && raw.selected.length > 0
          ? raw.selected
          : undefined,
      ledger:
        typeof raw.ledger === 'string' && raw.ledger.length > 0 ? raw.ledger : undefined,
    };
  },
  component: InventoryRouteComponent,
});

// ── URL schema ───────────────────────────────────────────────────────────

export interface InventorySearch extends Record<string, unknown> {
  search?: string;
  sort?: SortColumn;
  sortDir?: SortDirection;
  page?: number;
  selected?: string;
  ledger?: string;
}

const INVENTORY_DEFAULTS: InventorySearch = {
  search: undefined,
  sort: undefined,
  sortDir: undefined,
  page: undefined,
  selected: undefined,
  ledger: undefined,
};

function isSortColumn(v: unknown): v is SortColumn {
  return v === 'sku' || v === 'available' || v === 'reserved';
}

function isSortDirection(v: unknown): v is SortDirection {
  return v === 'asc' || v === 'desc';
}

function toPositiveInt(v: unknown): number | null {
  if (typeof v === 'number' && Number.isInteger(v) && v >= 1) return v;
  if (typeof v === 'string' && /^\d+$/.test(v)) {
    const n = Number(v);
    if (n >= 1) return n;
  }
  return null;
}

// ── Component ────────────────────────────────────────────────────────────

function InventoryRouteComponent() {
  useLocale();

  const [search, setSearch] = useFilterSearchParams<InventorySearch>(
    INVENTORY_DEFAULTS,
    {
      from: '/_auth/inventory',
      // Changing search / sort / sortDir auto-resets page + clears ledger.
      resetOn: ['search', 'sort', 'sortDir'],
      pageKey: 'page',
      ledgerKey: 'ledger',
    },
  );

  // Adjust + Create modals remain local — transient action dialogs are not
  // an addressable URL state.
  const [adjustingSku, setAdjustingSku] = useState<string | null>(null);
  const [isCreateOpen, setIsCreateOpen] = useState(false);

  const params = useMemo(
    () => ({ search: search.search ?? undefined, pageSize: 100 }),
    [search.search],
  );
  const { data, isLoading, isError } = useInventoryQuery(params);

  const items = useMemo(() => {
    const rows = data?.items ?? [];
    if (!search.sort) return rows;
    const sorted = [...rows];
    const dir = search.sortDir === 'desc' ? -1 : 1;
    sorted.sort((a, b) => {
      if (search.sort === 'sku') {
        return a.sku.localeCompare(b.sku) * dir;
      }
      if (search.sort === 'available') {
        return (a.available - b.available) * dir;
      }
      if (search.sort === 'reserved') {
        return (a.reserved - b.reserved) * dir;
      }
      return 0;
    });
    return sorted;
  }, [data, search.sort, search.sortDir]);

  // The drawer's open state mirrors the URL: when `?selected=` is present
  // we look the item up in the loaded list. If it's not found (stale or
  // unknown SKU) we still mount the drawer with `item=null` plus the SKU,
  // so the drawer renders its D-005 error state. U6 (next round) wires
  // the ledger cursor consumer; for now the `ledger` value is passed
  // through but the LedgerDrawer does not yet read it.
  const selectedItem = useMemo(
    () =>
      search.selected ? (data?.items ?? []).find((i) => i.sku === search.selected) ?? null : null,
    [data, search.selected],
  );
  const isUnknownSelectedSku =
    search.selected != null && !isLoading && !selectedItem;

  const handleSearchChange = useCallback(
    (value: string) => {
      setSearch({ search: value || undefined });
    },
    [setSearch],
  );

  const handleSortClick = useCallback(
    (column: SortColumn) => {
      // Click cycles asc → desc → off (undefined). When switching columns,
      // start at asc.
      const isSameColumn = search.sort === column;
      const nextDir: SortDirection | undefined = !isSameColumn
        ? 'asc'
        : search.sortDir === 'asc'
          ? 'desc'
          : undefined;
      setSearch({
        sort: nextDir == null ? undefined : column,
        sortDir: nextDir,
      });
    },
    [search.sort, search.sortDir, setSearch],
  );

  const handleSelectSku = useCallback(
    (sku: string) => {
      // Opening the drawer for a new SKU also resets the ledger cursor
      // (the previous SKU's cursor is meaningless against the new one).
      setSearch({ selected: sku, ledger: undefined });
    },
    [setSearch],
  );

  const closeDrawer = useCallback(() => {
    setSearch({ selected: undefined, ledger: undefined });
  }, [setSearch]);

  const openAdjust = useCallback((sku: string) => setAdjustingSku(sku), []);
  const closeAdjust = useCallback(() => setAdjustingSku(null), []);
  const openCreate = useCallback(() => setIsCreateOpen(true), []);
  const closeCreate = useCallback(() => setIsCreateOpen(false), []);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      <KpiStrip />
      <FilterStrip
        search={search.search ?? ''}
        onSearchChange={handleSearchChange}
        onCreateSkuClick={openCreate}
      />
      {isError ? (
        <ErrorState />
      ) : (
        <SkuTable
          items={items}
          onSelectSku={handleSelectSku}
          selectedSku={search.selected ?? null}
          isLoading={isLoading}
          sortColumn={search.sort}
          sortDirection={search.sortDir}
          onSortChange={handleSortClick}
        />
      )}
      <LedgerDrawer
        item={selectedItem}
        selectedSku={search.selected ?? null}
        isUnknownSelectedSku={isUnknownSelectedSku}
        ledgerCursor={search.ledger ?? null}
        onClose={closeDrawer}
        onAdjustClick={openAdjust}
      />
      <AdjustStockModal
        isOpen={adjustingSku !== null}
        sku={adjustingSku ?? ''}
        onClose={closeAdjust}
      />
      <CreateSkuModal isOpen={isCreateOpen} onClose={closeCreate} />
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
